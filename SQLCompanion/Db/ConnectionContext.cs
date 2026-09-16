using System;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace SQLCompanion.Db
{
    /// <summary>
    /// Obtains the server/database of the *currently active* SSMS query window.
    ///
    /// >>>>>>>>>>  HIGHLY VERSION-FRAGILE — VERIFY AGAINST YOUR SSMS 20 BUILD  <<<<<<<<<<
    /// SSMS exposes the active window's connection through an UNDOCUMENTED object graph:
    ///
    ///     Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache          (static)
    ///         .ScriptFactory                                                    (IScriptFactory)
    ///         .CurrentlyActiveWndConnectionInfo                                 (window conn info)
    ///         .UIConnectionInfo                                                 (UIConnectionInfo)
    ///             .ServerName / .UserName / .Password / .AuthenticationType
    ///             .AdvancedOptions["DATABASE"]                                  (current database)
    ///
    /// The exact member names/namespaces have shifted between SSMS versions, so instead of a
    /// compile-time reference we use REFLECTION over the assemblies SSMS has already loaded into
    /// the process. If any hop fails we return null and the UI shows a friendly "no connection"
    /// message. When you test in SSMS 20, if the connection isn't detected, set a breakpoint in
    /// TryGetFromScriptFactory and inspect the real member names, then adjust the strings below.
    /// </summary>
    internal static class ConnectionContext
    {
        /// <summary>
        /// Returns the active connection info, or null if none could be determined.
        /// Never throws.
        /// </summary>
        public static ActiveConnectionInfo TryGetActiveConnection()
        {
            try
            {
                var info = TryGetFromScriptFactory();
                if (info != null && info.IsValid)
                    return info;
            }
            catch
            {
                // Swallow — reflection into an undocumented API. Caller treats null as "no connection".
            }
            return null;
        }

        private static ActiveConnectionInfo TryGetFromScriptFactory()
        {
            // 1) Locate the ServiceCache type among the assemblies SSMS has loaded.
            //    (Inside SSMS the VSIntegration assembly is already loaded, so we don't need a
            //     compile-time reference or an explicit Assembly.Load.)
            Type serviceCache = FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
            if (serviceCache == null) return null;

            // 2) ServiceCache.ScriptFactory (static property).
            object scriptFactory = GetStaticProp(serviceCache, "ScriptFactory");
            if (scriptFactory == null) return null;

            // 3) scriptFactory.CurrentlyActiveWndConnectionInfo
            object wndConnInfo = GetProp(scriptFactory, "CurrentlyActiveWndConnectionInfo");
            if (wndConnInfo == null) return null; // no query window / not connected

            // 4) .UIConnectionInfo
            object uic = GetProp(wndConnInfo, "UIConnectionInfo");
            if (uic == null) return null;

            // 5) Pull the individual fields.
            string server = GetProp(uic, "ServerName") as string;
            string user = GetProp(uic, "UserName") as string;
            string password = GetProp(uic, "Password") as string;
            object authTypeObj = GetProp(uic, "AuthenticationType"); // int-ish; 0 == Windows in most builds

            // Database lives in the AdvancedOptions collection under the "DATABASE" key.
            string database = GetAdvancedOption(uic, "DATABASE");
            if (string.IsNullOrEmpty(database))
                database = GetProp(uic, "DatabaseName") as string; // alternate member seen on some builds
            if (string.IsNullOrEmpty(database))
                database = "master"; // sensible fallback; queries can still USE another db explicitly

            if (string.IsNullOrEmpty(server))
                return null;

            bool integrated = IsIntegratedSecurity(authTypeObj, user);

            var csb = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                ApplicationName = "SQL Companion (SSMS)",
                ConnectTimeout = 15,
                Pooling = true
            };

            if (integrated)
            {
                csb.IntegratedSecurity = true;
            }
            else
            {
                csb.UserID = user ?? string.Empty;
                csb.Password = password ?? string.Empty;
                // NOTE: SSMS may hand back an empty password for saved connections; in that case
                // integrated security or an interactive re-auth may be required. Flagged for testing.
            }

            // SSMS commonly connects with encryption; be permissive so we don't fail on self-signed certs.
            TrySet(csb, "Encrypt", true);
            TrySet(csb, "TrustServerCertificate", true);

            return new ActiveConnectionInfo
            {
                Server = server,
                Database = database,
                ConnectionString = csb.ConnectionString
            };
        }

        /// <summary>
        /// Heuristic: Windows/integrated auth when AuthenticationType == 0 (its usual value) or when
        /// there is no user name. Everything else is treated as user/password (SQL auth). Azure AD
        /// modes are NOT specially handled here — flag for testing if you use them.
        /// </summary>
        private static bool IsIntegratedSecurity(object authTypeObj, string user)
        {
            if (authTypeObj != null)
            {
                try
                {
                    int authType = Convert.ToInt32(authTypeObj);
                    if (authType == 0) return true;  // Windows Authentication (typical enum value)
                }
                catch { /* fall through to user-name heuristic */ }
            }
            return string.IsNullOrEmpty(user);
        }

        // ---- reflection helpers -------------------------------------------------

        private static Type FindType(string fullName)
        {
            // Search already-loaded assemblies first (fast, no side effects).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = SafeGetType(asm, fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static Type SafeGetType(Assembly asm, string fullName)
        {
            try { return asm.GetType(fullName, throwOnError: false); }
            catch { return null; }
        }

        private static object GetStaticProp(Type type, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p?.GetValue(null, null);
        }

        private static object GetProp(object instance, string name)
        {
            if (instance == null) return null;
            var p = instance.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(instance, null);
        }

        private static string GetAdvancedOption(object uic, string key)
        {
            try
            {
                object advanced = GetProp(uic, "AdvancedOptions");
                if (advanced == null) return null;

                // AdvancedOptions typically exposes a string indexer: this[string] => string.
                var indexer = advanced.GetType().GetProperty("Item",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, typeof(string), new[] { typeof(string) }, null);
                if (indexer != null)
                    return indexer.GetValue(advanced, new object[] { key }) as string;
            }
            catch { /* ignore */ }
            return null;
        }

        private static void TrySet(SqlConnectionStringBuilder csb, string keyword, object value)
        {
            try { csb[keyword] = value; } catch { /* older System.Data.SqlClient may not know the keyword */ }
        }
    }
}
