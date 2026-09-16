/* ============================================================================
   TestDatabase.sql  —  sample databases for exercising the SQL Companion add-in
   ============================================================================
   Run the whole script in SSMS 20 (it drops and recreates the databases, so it
   is safe to re-run). It builds TWO databases on purpose:

     • SqlCompanionTestDB   — the main schema-rich database
     • SqlCompanionTestDB2  — a second database that REUSES the same column names,
                              so you can test Column Search's "All databases" scope.

   What each feature can be tested against:

   1) COLUMN SEARCH (Tools ▸ SQL Companion ▸ Column Search)
      - Column names are deliberately REPEATED across many tables/views and both
        databases, e.g.:
           Name         -> Customer, Product, Category, Warehouse, Employee, Department, Client
           CustomerId   -> Customer (PK) and Order (FK), plus a view
           ProductId    -> Product (PK), OrderItem, Stock, StockMovement, a view
           CreatedDate  -> almost every table (both databases)
           Status       -> Customer, Order, Product, Employee, Invoice
           Email/Phone  -> Customer, Employee, Client
      - Multiple schemas (sales / hr / inventory / dbo) test the Schema filter.
      - Views (vw_OrderSummary, vw_ProductStock) test that views are included.
      - Try search term "id", "name", "date", "customer", "status" and toggle the
        scope to "All databases" to see cross-database hits.

   2) RELATIONSHIPS (Tools ▸ SQL Companion ▸ Relationships, or "Use Active")
      - Foreign keys of every shape:
           self-referencing : Employee.ManagerId -> Employee, Category.ParentCategoryId -> Category
           cross-schema     : Order.SalesRepId -> hr.Employee, OrderItem.ProductId -> inventory.Product
           composite (2-col): StockMovement(WarehouseId,ProductId) -> Stock(WarehouseId,ProductId)
           both directions  : e.g. Customer is referenced-by Order; Order references Customer
      - Load "sales.Order", "sales.Customer", "inventory.Product", "hr.Employee",
        or "inventory.Stock" and double-click a row to insert a JOIN.

   3) DECLARE VARIABLES (editor right-click, or the submenu)
      - A sample query with undeclared @variables is at the very bottom (commented).
        Paste it into a query window and run the command to auto-add DECLAREs.
   ============================================================================ */


/* ========================= DATABASE 1 ===================================== */
IF DB_ID('SqlCompanionTestDB') IS NOT NULL
BEGIN
    ALTER DATABASE SqlCompanionTestDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlCompanionTestDB;
END
GO
CREATE DATABASE SqlCompanionTestDB;
GO
USE SqlCompanionTestDB;
GO

CREATE SCHEMA sales;
GO
CREATE SCHEMA hr;
GO
CREATE SCHEMA inventory;
GO

/* ---- Tables (created first; foreign keys added afterwards) ---------------- */

CREATE TABLE hr.Department
(
    DepartmentId INT IDENTITY(1,1) CONSTRAINT PK_Department PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL,
    IsActive     BIT           NOT NULL CONSTRAINT DF_Department_IsActive   DEFAULT (1),
    CreatedDate  DATETIME2     NOT NULL CONSTRAINT DF_Department_CreatedDate DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE hr.Employee
(
    EmployeeId   INT IDENTITY(1,1) CONSTRAINT PK_Employee PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL,
    Email        NVARCHAR(256) NULL,
    Phone        NVARCHAR(40)  NULL,
    ManagerId    INT           NULL,   -- self-referencing FK
    DepartmentId INT           NULL,   -- FK -> hr.Department
    HireDate     DATE          NOT NULL CONSTRAINT DF_Employee_HireDate    DEFAULT (SYSUTCDATETIME()),
    Status       NVARCHAR(20)  NOT NULL CONSTRAINT DF_Employee_Status      DEFAULT ('Active'),
    IsActive     BIT           NOT NULL CONSTRAINT DF_Employee_IsActive    DEFAULT (1),
    CreatedDate  DATETIME2     NOT NULL CONSTRAINT DF_Employee_CreatedDate DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE inventory.Category
(
    CategoryId       INT IDENTITY(1,1) CONSTRAINT PK_Category PRIMARY KEY,
    Name             NVARCHAR(100) NOT NULL,
    ParentCategoryId INT           NULL,   -- self-referencing FK
    IsActive         BIT           NOT NULL CONSTRAINT DF_Category_IsActive    DEFAULT (1),
    CreatedDate      DATETIME2     NOT NULL CONSTRAINT DF_Category_CreatedDate DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE inventory.Product
(
    ProductId    INT IDENTITY(1,1) CONSTRAINT PK_Product PRIMARY KEY,
    Name         NVARCHAR(150)  NOT NULL,
    Sku          NVARCHAR(50)   NOT NULL,
    CategoryId   INT            NULL,   -- FK -> inventory.Category
    UnitPrice    DECIMAL(18,2)  NOT NULL CONSTRAINT DF_Product_UnitPrice    DEFAULT (0),
    Status       NVARCHAR(20)   NOT NULL CONSTRAINT DF_Product_Status       DEFAULT ('Available'),
    IsActive     BIT            NOT NULL CONSTRAINT DF_Product_IsActive     DEFAULT (1),
    CreatedDate  DATETIME2      NOT NULL CONSTRAINT DF_Product_CreatedDate  DEFAULT (SYSUTCDATETIME()),
    ModifiedDate DATETIME2      NULL
);

CREATE TABLE inventory.Warehouse
(
    WarehouseId INT IDENTITY(1,1) CONSTRAINT PK_Warehouse PRIMARY KEY,
    Name        NVARCHAR(100) NOT NULL,
    Location    NVARCHAR(200) NULL,
    IsActive    BIT           NOT NULL CONSTRAINT DF_Warehouse_IsActive    DEFAULT (1),
    CreatedDate DATETIME2     NOT NULL CONSTRAINT DF_Warehouse_CreatedDate DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE inventory.Stock
(
    WarehouseId  INT NOT NULL,          -- part of composite PK, FK -> Warehouse
    ProductId    INT NOT NULL,          -- part of composite PK, FK -> Product
    Quantity     INT NOT NULL CONSTRAINT DF_Stock_Quantity DEFAULT (0),
    ModifiedDate DATETIME2 NOT NULL CONSTRAINT DF_Stock_ModifiedDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Stock PRIMARY KEY (WarehouseId, ProductId)
);

CREATE TABLE inventory.StockMovement
(
    MovementId   INT IDENTITY(1,1) CONSTRAINT PK_StockMovement PRIMARY KEY,
    WarehouseId  INT NOT NULL,          -- composite FK (WarehouseId, ProductId) -> Stock
    ProductId    INT NOT NULL,
    Quantity     INT NOT NULL,
    MovementDate DATETIME2 NOT NULL CONSTRAINT DF_StockMovement_Date DEFAULT (SYSUTCDATETIME()),
    Notes        NVARCHAR(400) NULL
);

CREATE TABLE sales.Customer
(
    CustomerId   INT IDENTITY(1,1) CONSTRAINT PK_Customer PRIMARY KEY,
    Name         NVARCHAR(150) NOT NULL,
    Email        NVARCHAR(256) NULL,
    Phone        NVARCHAR(40)  NULL,
    Status       NVARCHAR(20)  NOT NULL CONSTRAINT DF_Customer_Status      DEFAULT ('Active'),
    IsActive     BIT           NOT NULL CONSTRAINT DF_Customer_IsActive    DEFAULT (1),
    CreatedDate  DATETIME2     NOT NULL CONSTRAINT DF_Customer_CreatedDate DEFAULT (SYSUTCDATETIME()),
    ModifiedDate DATETIME2     NULL
);

CREATE TABLE sales.[Order]
(
    OrderId      INT IDENTITY(1,1) CONSTRAINT PK_Order PRIMARY KEY,
    CustomerId   INT           NOT NULL,   -- FK -> sales.Customer
    SalesRepId   INT           NULL,       -- cross-schema FK -> hr.Employee
    OrderDate    DATETIME2     NOT NULL CONSTRAINT DF_Order_OrderDate    DEFAULT (SYSUTCDATETIME()),
    Status       NVARCHAR(20)  NOT NULL CONSTRAINT DF_Order_Status       DEFAULT ('New'),
    TotalAmount  DECIMAL(18,2) NOT NULL CONSTRAINT DF_Order_TotalAmount  DEFAULT (0),
    CreatedDate  DATETIME2     NOT NULL CONSTRAINT DF_Order_CreatedDate  DEFAULT (SYSUTCDATETIME()),
    ModifiedDate DATETIME2     NULL
);

CREATE TABLE sales.OrderItem
(
    OrderId   INT NOT NULL,              -- part of composite PK, FK -> sales.Order
    [LineNo]    INT NOT NULL,
    ProductId INT NOT NULL,              -- cross-schema FK -> inventory.Product
    Quantity  INT NOT NULL CONSTRAINT DF_OrderItem_Quantity  DEFAULT (1),
    UnitPrice DECIMAL(18,2) NOT NULL CONSTRAINT DF_OrderItem_UnitPrice DEFAULT (0),
    CONSTRAINT PK_OrderItem PRIMARY KEY (OrderId, [LineNo])
);
GO

/* ---- Foreign keys -------------------------------------------------------- */
ALTER TABLE hr.Employee        ADD CONSTRAINT FK_Employee_Manager    FOREIGN KEY (ManagerId)    REFERENCES hr.Employee (EmployeeId);
ALTER TABLE hr.Employee        ADD CONSTRAINT FK_Employee_Department FOREIGN KEY (DepartmentId) REFERENCES hr.Department (DepartmentId);
ALTER TABLE inventory.Category ADD CONSTRAINT FK_Category_Parent     FOREIGN KEY (ParentCategoryId) REFERENCES inventory.Category (CategoryId);
ALTER TABLE inventory.Product  ADD CONSTRAINT FK_Product_Category    FOREIGN KEY (CategoryId)   REFERENCES inventory.Category (CategoryId);
ALTER TABLE inventory.Stock    ADD CONSTRAINT FK_Stock_Warehouse     FOREIGN KEY (WarehouseId)  REFERENCES inventory.Warehouse (WarehouseId);
ALTER TABLE inventory.Stock    ADD CONSTRAINT FK_Stock_Product       FOREIGN KEY (ProductId)    REFERENCES inventory.Product (ProductId);
ALTER TABLE inventory.StockMovement ADD CONSTRAINT FK_StockMovement_Stock FOREIGN KEY (WarehouseId, ProductId) REFERENCES inventory.Stock (WarehouseId, ProductId);
ALTER TABLE sales.[Order]      ADD CONSTRAINT FK_Order_Customer      FOREIGN KEY (CustomerId)   REFERENCES sales.Customer (CustomerId);
ALTER TABLE sales.[Order]      ADD CONSTRAINT FK_Order_SalesRep      FOREIGN KEY (SalesRepId)   REFERENCES hr.Employee (EmployeeId);
ALTER TABLE sales.OrderItem    ADD CONSTRAINT FK_OrderItem_Order     FOREIGN KEY (OrderId)      REFERENCES sales.[Order] (OrderId);
ALTER TABLE sales.OrderItem    ADD CONSTRAINT FK_OrderItem_Product   FOREIGN KEY (ProductId)    REFERENCES inventory.Product (ProductId);
GO

/* ---- Views (also carry repeated column names) ---------------------------- */
GO
CREATE VIEW sales.vw_OrderSummary
AS
    SELECT o.OrderId,
           o.CustomerId,
           c.Name AS CustomerName,
           o.OrderDate,
           o.Status,
           o.TotalAmount
    FROM sales.[Order] o
    JOIN sales.Customer c ON c.CustomerId = o.CustomerId;
GO
CREATE VIEW inventory.vw_ProductStock
AS
    SELECT p.ProductId,
           p.Name,
           s.WarehouseId,
           s.Quantity,
           p.Status
    FROM inventory.Product p
    JOIN inventory.Stock s ON s.ProductId = p.ProductId;
GO

/* ---- Sample data (identity values are deterministic in a fresh DB) -------- */
INSERT INTO hr.Department (Name) VALUES (N'Sales'), (N'IT'), (N'Warehouse');

INSERT INTO hr.Employee (Name, Email, Phone, ManagerId, DepartmentId)
VALUES (N'Alice Manager', N'alice@example.com', N'555-0100', NULL, 1),
       (N'Bob Rep',       N'bob@example.com',   N'555-0101', 1,    1),
       (N'Carol Stock',   N'carol@example.com', N'555-0102', 1,    3);

INSERT INTO inventory.Category (Name, ParentCategoryId)
VALUES (N'Electronics', NULL), (N'Phones', 1), (N'Accessories', 1);

INSERT INTO inventory.Product (Name, Sku, CategoryId, UnitPrice)
VALUES (N'Smartphone X', N'PH-X',   2, 799.00),
       (N'USB Cable',    N'AC-USB', 3,   9.50),
       (N'Laptop Z',     N'EL-LZ',  1, 1299.00);

INSERT INTO inventory.Warehouse (Name, Location) VALUES (N'Main', N'HQ'), (N'East', N'Port');

INSERT INTO sales.Customer (Name, Email, Phone)
VALUES (N'Contoso Ltd',  N'ap@contoso.com',  N'555-0200'),
       (N'Fabrikam Inc', N'ap@fabrikam.com', N'555-0201');

INSERT INTO sales.[Order] (CustomerId, SalesRepId, Status, TotalAmount)
VALUES (1, 2, N'Shipped', 1607.50),
       (2, 2, N'New',     1299.00);

INSERT INTO sales.OrderItem (OrderId, [LineNo], ProductId, Quantity, UnitPrice)
VALUES (1, 1, 1, 2,  799.00),
       (1, 2, 2, 1,    9.50),
       (2, 1, 3, 1, 1299.00);

INSERT INTO inventory.Stock (WarehouseId, ProductId, Quantity)
VALUES (1, 1, 100), (1, 2, 500), (2, 3, 20);

INSERT INTO inventory.StockMovement (WarehouseId, ProductId, Quantity, Notes)
VALUES (1, 1, -2, N'Order 1'), (2, 3, -1, N'Order 2');
GO


/* ========================= DATABASE 2 ===================================== */
/* Reuses column names (Name, Email, Status, CreatedDate, plus a FK) so you can
   test Column Search with the scope set to "All databases".                  */
IF DB_ID('SqlCompanionTestDB2') IS NOT NULL
BEGIN
    ALTER DATABASE SqlCompanionTestDB2 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlCompanionTestDB2;
END
GO
CREATE DATABASE SqlCompanionTestDB2;
GO
USE SqlCompanionTestDB2;
GO

CREATE TABLE dbo.Client
(
    ClientId    INT IDENTITY(1,1) CONSTRAINT PK_Client PRIMARY KEY,
    Name        NVARCHAR(150) NOT NULL,
    Email       NVARCHAR(256) NULL,
    Status      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Client_Status      DEFAULT ('Active'),
    CreatedDate DATETIME2     NOT NULL CONSTRAINT DF_Client_CreatedDate DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.Invoice
(
    InvoiceId   INT IDENTITY(1,1) CONSTRAINT PK_Invoice PRIMARY KEY,
    ClientId    INT           NOT NULL,   -- FK -> dbo.Client
    Amount      DECIMAL(18,2) NOT NULL CONSTRAINT DF_Invoice_Amount      DEFAULT (0),
    Status      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Invoice_Status      DEFAULT ('Open'),
    CreatedDate DATETIME2     NOT NULL CONSTRAINT DF_Invoice_CreatedDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Invoice_Client FOREIGN KEY (ClientId) REFERENCES dbo.Client (ClientId)
);
GO

INSERT INTO dbo.Client (Name, Email) VALUES (N'Northwind', N'ap@northwind.com'), (N'Adventure Works', N'ap@aw.com');
INSERT INTO dbo.Invoice (ClientId, Amount, Status) VALUES (1, 250.00, N'Open'), (2, 999.00, N'Paid');
GO

PRINT 'Created SqlCompanionTestDB and SqlCompanionTestDB2 with sample data.';
GO


/* ============================================================================
   DECLARE-VARIABLES TEST SNIPPET
   ----------------------------------------------------------------------------
   Open a NEW query window against SqlCompanionTestDB, paste the lines below
   (WITHOUT the surrounding comment markers), select them, then run
   Tools ▸ SQL Companion ▸ Declare Variables (or the editor right-click command).
   It should add DECLARE statements for @customerId, @status and @minTotal.

     SET @customerId = 1;
     SET @status = N'Shipped';
     SET @minTotal = 1000;

     SELECT o.OrderId, o.Status, o.TotalAmount, c.Name
     FROM sales.[Order] o
     JOIN sales.Customer c ON c.CustomerId = o.CustomerId
     WHERE o.CustomerId = @customerId
       AND o.Status = @status
       AND o.TotalAmount >= @minTotal;
   ============================================================================ */
