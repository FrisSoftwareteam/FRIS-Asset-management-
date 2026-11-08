using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using MongoDB.Driver;

namespace FrislEams.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await RunSectionAsync("required user accounts", () => UserAccountRepository.EnsureRequiredUsersAsync(db, cancellationToken));

        await RunSectionAsync("indexes", () => db.EnsureIndexesAsync(cancellationToken));
        await RunSectionAsync("legacy user migration", () => MigrateLegacyUserAccountsAsync(db, cancellationToken));
        RunSection("categories", () => SeedCategories(db));
        RunSection("asset types", () => SeedAssetTypes(db));
        RunSection("manufacturers", () => SeedManufacturers(db));
        RunSection("locations", () => SeedLocations(db));
        RunSection("departments", () => SeedDepartments(db));
        RunSection("suppliers", () => SeedSuppliers(db));
        RunSection("contractors", () => SeedContractors(db));
        RunSection("staff", () => SeedStaff(db));
        await RunSectionAsync("required staff", () => StaffRepository.EnsureRequiredStaffAsync(db, cancellationToken));
        RunSection("user accounts", () => SeedUserAccounts(db));
        RunSection("assets", () => SeedAssets(db));
        // After assets exist (and on every restart): keep Emmanuel's Ubiquiti AP awaiting receipt.
        await RunSectionAsync("demo pending receipt", () => AssignmentRepository.EnsureDemoPendingReceiptAsync(db, cancellationToken));

        var userCount = await db.CountUsersAsync(cancellationToken);
        Console.WriteLine($"FRISL EAMS startup: {userCount} users in database.");
    }

    private static async Task RunSectionAsync(string name, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FRISL EAMS startup warning: {name} failed: {ex.Message}");
        }
    }

    private static void RunSection(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FRISL EAMS startup warning: {name} failed: {ex.Message}");
        }
    }

    public static void Initialize(AppDbContext db) => InitializeAsync(db).GetAwaiter().GetResult();

    private static async Task MigrateLegacyUserAccountsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var usersCollection = db.Users.Collection;
        var usersCount = await usersCollection.CountDocumentsAsync(
            FilterDefinition<UserAccount>.Empty,
            cancellationToken: cancellationToken);
        if (usersCount > 0)
        {
            return;
        }

        var legacyCollection = usersCollection.Database.GetCollection<UserAccount>("userAccounts");
        var legacyUsers = await legacyCollection
            .Find(FilterDefinition<UserAccount>.Empty)
            .ToListAsync(cancellationToken);

        if (legacyUsers.Count == 0)
        {
            return;
        }

        db.Users.AddRange(legacyUsers);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void SeedCategories(AppDbContext db)
    {
        if (db.AssetCategories.Any()) return;
        db.AssetCategories.AddRange([
            new AssetCategory { Name = "Computers & Laptops", Code = "CMP", UsefulLifeYears = 4 },
            new AssetCategory { Name = "Furniture & Fittings", Code = "FUR", UsefulLifeYears = 10 },
            new AssetCategory { Name = "Office Equipment", Code = "OEQ", UsefulLifeYears = 5 },
            new AssetCategory { Name = "Vehicles", Code = "VEH", UsefulLifeYears = 7 },
            new AssetCategory { Name = "Networking Equipment", Code = "NET", UsefulLifeYears = 5 },
            new AssetCategory { Name = "Security Equipment", Code = "SEC", UsefulLifeYears = 6 },
            new AssetCategory { Name = "Printers & Scanners", Code = "PRT", UsefulLifeYears = 5 },
            new AssetCategory { Name = "Server & Storage", Code = "SRV", UsefulLifeYears = 6 },
            new AssetCategory { Name = "Communication Devices", Code = "COM", UsefulLifeYears = 3 },
            new AssetCategory { Name = "Air Conditioners", Code = "ACU", UsefulLifeYears = 8 }
        ]);
        db.SaveChanges();
    }

    private static void SeedAssetTypes(AppDbContext db)
    {
        if (db.AssetTypes.Any()) return;
        var cats = db.AssetCategories.AsQueryable().ToDictionary(c => c.Code, c => c.Id);
        db.AssetTypes.AddRange([
            new AssetType { Name = "Laptop", Code = "LAP", AssetCategoryId = cats["CMP"] },
            new AssetType { Name = "Desktop", Code = "DSK", AssetCategoryId = cats["CMP"] },
            new AssetType { Name = "Office Chair", Code = "CHR", AssetCategoryId = cats["FUR"] },
            new AssetType { Name = "Network Switch", Code = "SWT", AssetCategoryId = cats["NET"] },
            new AssetType { Name = "Laser Printer", Code = "LPR", AssetCategoryId = cats["PRT"] }
        ]);
        db.SaveChanges();
    }

    private static void SeedManufacturers(AppDbContext db)
    {
        if (db.Manufacturers.Any()) return;
        db.Manufacturers.AddRange([
            new Manufacturer { Name = "Dell", Code = "DELL" },
            new Manufacturer { Name = "HP", Code = "HP" },
            new Manufacturer { Name = "Lenovo", Code = "LEN" },
            new Manufacturer { Name = "Cisco", Code = "CSCO" },
            new Manufacturer { Name = "Apple", Code = "APPL" }
        ]);
        db.SaveChanges();
    }

    private static void SeedLocations(AppDbContext db)
    {
        if (db.Locations.Any()) return;
        db.Locations.AddRange([
            new Location { Name = "Head Office – Abuja", Code = "HQ-ABJ", LocationType = "Branch" },
            new Location { Name = "Lagos Branch", Code = "LG-BRN", LocationType = "Branch" },
            new Location { Name = "Ibadan Branch", Code = "IB-BRN", LocationType = "Branch" },
            new Location { Name = "Port Harcourt Branch", Code = "PH-BRN", LocationType = "Branch" },
            new Location { Name = "HQ Main Building", Code = "HQ-BLD", LocationType = "Building", ParentLocationId = null },
            new Location { Name = "Ground Floor", Code = "HQ-GF", LocationType = "Floor" },
            new Location { Name = "Server Room", Code = "SRV-HQ", LocationType = "Room" },
            new Location { Name = "Central Storage", Code = "STORE", LocationType = "Room" },
            new Location { Name = "Archives", Code = "ARCH", LocationType = "Room" }
        ]);
        db.SaveChanges();
    }

    private static void SeedDepartments(AppDbContext db)
    {
        if (db.Departments.Any()) return;
        db.Departments.AddRange([
            new Department { Name = "Information Technology", Code = "IT" },
            new Department { Name = "Finance & Accounts", Code = "FIN" },
            new Department { Name = "Human Resources", Code = "HR" },
            new Department { Name = "Operations", Code = "OPS" },
            new Department { Name = "Compliance & Legal", Code = "LEG" },
            new Department { Name = "Internal Audit", Code = "AUD" },
            new Department { Name = "Investor Services", Code = "INV" },
            new Department { Name = "Corporate Services", Code = "CRP" }
        ]);
        db.SaveChanges();
    }

    private static void SeedSuppliers(AppDbContext db)
    {
        if (db.Suppliers.Any()) return;
        db.Suppliers.AddRange([
            new Supplier { Name = "TechMart Nigeria Ltd", Code = "TECHM", ContactPerson = "Emeka Obi", Phone = "0801-234-5678", Email = "emeka@techmart.ng", Address = "Lagos Island, Lagos" },
            new Supplier { Name = "FurniGlobe Interiors", Code = "FURNI", ContactPerson = "Amina Yusuf", Phone = "0802-345-6789", Email = "amina@furniglobe.ng", Address = "Wuse II, Abuja" },
            new Supplier { Name = "NetPro Solutions", Code = "NETPRO", ContactPerson = "Chidi Nwosu", Phone = "0803-456-7890", Email = "chidi@netpro.ng", Address = "Victoria Island, Lagos" },
            new Supplier { Name = "SecurePoint Systems", Code = "SECPT", ContactPerson = "Halima Musa", Phone = "0804-567-8901", Email = "halima@securepoint.ng", Address = "Garki, Abuja" }
        ]);
        db.SaveChanges();
    }

    private static void SeedContractors(AppDbContext db)
    {
        if (db.RepairContractors.Any()) return;
        db.RepairContractors.AddRange([
            new RepairContractor { Name = "QuickFix Tech Services", Code = "QFIX", ContactPerson = "Tunde Adeyemi", Phone = "0805-678-9012", Email = "tunde@quickfix.ng", Specialisation = "Computers & Networking", SlaHours = "24" },
            new RepairContractor { Name = "PowerCool HVAC", Code = "PCOOL", ContactPerson = "Grace Okafor", Phone = "0806-789-0123", Email = "grace@powercool.ng", Specialisation = "Air Conditioners", SlaHours = "48" },
            new RepairContractor { Name = "AllFix Engineering", Code = "ALFIX", ContactPerson = "Ibrahim Aliyu", Phone = "0807-890-1234", Email = "ibrahim@allfixeng.ng", Specialisation = "General Repairs", SlaHours = "72" }
        ]);
        db.SaveChanges();
    }

    private static void SeedStaff(AppDbContext db)
    {
        if (db.Staff.Any()) return;
        var depts = db.Departments.AsQueryable().ToDictionary(d => d.Code, d => d.Id);
        db.Staff.AddRange([
            new Staff { StaffId = "FR-001", FullName = "Adaeze Okonkwo", Email = "adaeze.o@firstregistrars.ng", PhoneNumber = "0811-111-1111", Role = "Backoffice", DepartmentId = depts["IT"] },
            new Staff { StaffId = "FR-002", FullName = "Bello Umar", Email = "bello.u@firstregistrars.ng", PhoneNumber = "0812-222-2222", Role = "DepartmentHead", DepartmentId = depts["FIN"] },
            new Staff { StaffId = "FR-003", FullName = "Chisom Eze", Email = "chisom.e@firstregistrars.ng", PhoneNumber = "0813-333-3333", Role = "Staff", DepartmentId = depts["HR"] },
            new Staff { StaffId = "FR-004", FullName = "Damilola Afolabi", Email = "damilola.a@firstregistrars.ng", PhoneNumber = "0814-444-4444", Role = "Auditor", DepartmentId = depts["AUD"] },
            new Staff { StaffId = "FR-005", FullName = "Emeka Chukwu", Email = "emeka.c@firstregistrars.ng", PhoneNumber = "0815-555-5555", Role = "Staff", DepartmentId = depts["OPS"] },
            new Staff { StaffId = "FR-006", FullName = "Fatima Sule", Email = "fatima.s@firstregistrars.ng", PhoneNumber = "0816-666-6666", Role = "DepartmentHead", DepartmentId = depts["OPS"] },
            new Staff { StaffId = "FR-007", FullName = "Gbenga Oluwole", Email = "gbenga.o@firstregistrars.ng", PhoneNumber = "0817-777-7777", Role = "Staff", DepartmentId = depts["IT"] },
            new Staff { StaffId = "FR-008", FullName = "Hauwa Abdullahi", Email = "hauwa.a@firstregistrars.ng", PhoneNumber = "0818-888-8888", Role = "Staff", DepartmentId = depts["LEG"] },
            new Staff { StaffId = "FR-009", FullName = "Emmanuel", Email = "emmanuel@firstregistrars.ng", PhoneNumber = "0819-999-9999", Role = "Staff", DepartmentId = depts["IT"] },
            new Staff { StaffId = "FR-010", FullName = "Abayomi", Email = "abayomi@firstregistrars.ng", PhoneNumber = "0820-000-0000", Role = "Staff", DepartmentId = depts["IT"] }
        ]);
        db.SaveChanges();
    }

    private static void SeedUserAccounts(AppDbContext db)
    {
        if (db.Users.Any()) return;

        db.Users.AddRange([
            new UserAccount { Username = "oludele.gbenro@firstregistrarsnigeria.com", Password = "Password@123", Role = "Backoffice", DisplayName = "Oludele Gbenro" },
            new UserAccount { Username = "Admin", Password = "Admin1", Role = "Admin", DisplayName = "Admin" },
            new UserAccount { Username = "Washington", Password = "Washington1", Role = "Backoffice", DisplayName = "Washington" },
            new UserAccount { Username = "Auditor", Password = "auditor1", Role = "Auditor", DisplayName = "Auditor" },
            new UserAccount { Username = "Staff", Password = "Staff1", Role = "Staff", DisplayName = "Staff" },
            new UserAccount { Username = "Staff1", Password = "January2021###", Role = "Staff", DisplayName = "Staff 1" },
            new UserAccount { Username = "Staff2", Password = "January2021###", Role = "Staff", DisplayName = "Staff 2" },
            new UserAccount { Username = "Staff3", Password = "January2021###", Role = "Staff", DisplayName = "Staff 3" },
            new UserAccount { Username = "Staff4", Password = "January2021###", Role = "Staff", DisplayName = "Staff 4" },
            new UserAccount { Username = "emmanuel", Password = "Washington", Role = "Staff", DisplayName = "Emmanuel" },
            new UserAccount { Username = "abayomi", Password = "Washington", Role = "Staff", DisplayName = "Abayomi" },
            new UserAccount { Username = "staff", Password = "staff123", Role = "Staff", DisplayName = "Staff Member" },
            new UserAccount { Username = "viewer", Password = "viewer123", Role = "Viewer", DisplayName = "Read-Only Viewer" }
        ]);
        db.SaveChanges();
    }

    private static void SeedAssets(AppDbContext db)
    {
        if (db.Assets.Any()) return;

        var cats = db.AssetCategories.AsQueryable().ToDictionary(c => c.Code, c => c);
        var locs = db.Locations.AsQueryable().ToDictionary(l => l.Code, l => l);
        var depts = db.Departments.AsQueryable().ToDictionary(d => d.Code, d => d);
        var staffList = db.Staff.AsQueryable().ToDictionary(s => s.StaffId, s => s);
        var suppliers = db.Suppliers.AsQueryable().ToDictionary(s => s.Code, s => s);

        var assets = new List<Asset>
        {
            new() { TagCode = "FRISL-2022-CMP-IT-001", AssetName = "Dell Latitude 5530 Laptop", Description = "Core i7, 16GB RAM, 512GB SSD", AssetCategoryId = cats["CMP"].Id, PurchaseDate = new DateTime(2022, 3, 15), PurchaseCost = 850000, GlCode = "5001", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "DL5530-NG-22001", Brand = "Dell", ModelNumber = "Latitude 5530", WarrantyExpiryDate = new DateTime(2025, 3, 15), ExpectedServiceYears = 4, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["IT"].Id, CurrentCustodianId = staffList["FR-007"].Id, CreatedAt = new DateTime(2022, 3, 16) },
            new() { TagCode = "FRISL-2022-CMP-FIN-001", AssetName = "HP EliteBook 840 G9", Description = "Core i5, 8GB RAM, 256GB SSD", AssetCategoryId = cats["CMP"].Id, PurchaseDate = new DateTime(2022, 6, 20), PurchaseCost = 680000, GlCode = "5001", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "HP840G9-NG-22002", Brand = "HP", ModelNumber = "EliteBook 840 G9", WarrantyExpiryDate = new DateTime(2025, 6, 20), ExpectedServiceYears = 4, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["FIN"].Id, CurrentCustodianId = staffList["FR-002"].Id, CreatedAt = new DateTime(2022, 6, 21) },
            new() { TagCode = "FRISL-2023-FUR-HR-001", AssetName = "Executive Office Chair", Description = "High-back leather executive chair", AssetCategoryId = cats["FUR"].Id, PurchaseDate = new DateTime(2023, 1, 10), PurchaseCost = 120000, GlCode = "5005", StateOfPurchase = "New", SupplierId = suppliers["FURNI"].Id, SerialNumber = "EOC-HR-23001", Brand = "Steelcase", CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["HR"].Id, CurrentCustodianId = staffList["FR-003"].Id, CreatedAt = new DateTime(2023, 1, 11) },
            new() { TagCode = "FRISL-2021-NET-IT-001", AssetName = "Cisco Catalyst 2960 Switch", Description = "48-port managed gigabit switch", AssetCategoryId = cats["NET"].Id, PurchaseDate = new DateTime(2021, 9, 5), PurchaseCost = 1200000, GlCode = "5003", StateOfPurchase = "New", SupplierId = suppliers["NETPRO"].Id, SerialNumber = "CISCO2960-NG-21001", Brand = "Cisco", ModelNumber = "Catalyst 2960", WarrantyExpiryDate = new DateTime(2024, 9, 5), ExpectedServiceYears = 5, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["SRV-HQ"].Id, CurrentDepartmentId = depts["IT"].Id, CurrentCustodianId = staffList["FR-001"].Id, CreatedAt = new DateTime(2021, 9, 6) },
            new() { TagCode = "FRISL-2023-PRT-OPS-001", AssetName = "HP LaserJet Pro M428fdn", Description = "Multifunction monochrome laser printer", AssetCategoryId = cats["PRT"].Id, PurchaseDate = new DateTime(2023, 4, 18), PurchaseCost = 285000, GlCode = "5002", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "HPLJM428-OPS-23001", Brand = "HP", ModelNumber = "LaserJet Pro M428fdn", WarrantyExpiryDate = new DateTime(2025, 4, 18), ExpectedServiceYears = 5, CurrentCondition = "Fair", CurrentStatus = AssetStatus.UnderRepair, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["OPS"].Id, CreatedAt = new DateTime(2023, 4, 19) },
            new() { TagCode = "FRISL-2022-ACU-FIN-001", AssetName = "Midea 2.5HP Split AC Unit", Description = "Inverter split type air conditioner", AssetCategoryId = cats["ACU"].Id, PurchaseDate = new DateTime(2022, 2, 28), PurchaseCost = 450000, GlCode = "5006", StateOfPurchase = "New", SerialNumber = "MID25HP-FIN-22001", Brand = "Midea", CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["FIN"].Id, CreatedAt = new DateTime(2022, 3, 1) },
            new() { TagCode = "FRISL-2023-CMP-OPS-001", AssetName = "Lenovo ThinkPad T14", Description = "AMD Ryzen 7, 16GB RAM, 512GB SSD", AssetCategoryId = cats["CMP"].Id, PurchaseDate = new DateTime(2023, 7, 12), PurchaseCost = 720000, GlCode = "5001", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "LNTPT14-OPS-23001", Brand = "Lenovo", ModelNumber = "ThinkPad T14", WarrantyExpiryDate = new DateTime(2026, 7, 12), ExpectedServiceYears = 4, CurrentCondition = "Good", CurrentStatus = AssetStatus.RegisteredUnassigned, CurrentLocationId = locs["STORE"].Id, CreatedAt = new DateTime(2023, 7, 13) },
            new() { TagCode = "FRISL-2021-SRV-IT-001", AssetName = "Dell PowerEdge R740 Server", Description = "2x Xeon Gold, 256GB RAM, 12TB SAS", AssetCategoryId = cats["SRV"].Id, PurchaseDate = new DateTime(2021, 3, 1), PurchaseCost = 8500000, GlCode = "5007", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "DPR740-IT-21001", Brand = "Dell", ModelNumber = "PowerEdge R740", WarrantyExpiryDate = new DateTime(2025, 3, 1), ExpectedServiceYears = 6, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["SRV-HQ"].Id, CurrentDepartmentId = depts["IT"].Id, CurrentCustodianId = staffList["FR-001"].Id, CreatedAt = new DateTime(2021, 3, 2) },
            new() { TagCode = "FRISL-2023-SEC-HQ-001", AssetName = "Hikvision IP Camera 4MP", Description = "Outdoor dome IP camera with night vision", AssetCategoryId = cats["SEC"].Id, PurchaseDate = new DateTime(2023, 5, 22), PurchaseCost = 95000, GlCode = "5008", StateOfPurchase = "New", SupplierId = suppliers["SECPT"].Id, SerialNumber = "HKV4MP-HQ-23001", Brand = "Hikvision", CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CreatedAt = new DateTime(2023, 5, 23) },
            new() { TagCode = "FRISL-2020-FUR-LEG-001", AssetName = "Boardroom Conference Table", Description = "12-seater oval executive conference table", AssetCategoryId = cats["FUR"].Id, PurchaseDate = new DateTime(2020, 8, 15), PurchaseCost = 850000, GlCode = "5005", StateOfPurchase = "New", SupplierId = suppliers["FURNI"].Id, SerialNumber = "BRDTBL-LEG-20001", Brand = "Ekornes", CurrentCondition = "Fair", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["LEG"].Id, CreatedAt = new DateTime(2020, 8, 16) },
            new() { TagCode = "FRISL-2024-CMP-AUD-001", AssetName = "Apple MacBook Pro 14\"", Description = "M3 Pro chip, 18GB RAM, 512GB SSD", AssetCategoryId = cats["CMP"].Id, PurchaseDate = new DateTime(2024, 1, 8), PurchaseCost = 1850000, GlCode = "5001", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "APMBP14-AUD-24001", Brand = "Apple", ModelNumber = "MacBook Pro 14", WarrantyExpiryDate = new DateTime(2025, 1, 8), ExpectedServiceYears = 4, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["AUD"].Id, CurrentCustodianId = staffList["FR-004"].Id, CreatedAt = new DateTime(2024, 1, 9) },
            new() { TagCode = "FRISL-2022-VEH-OPS-001", AssetName = "Toyota Hilux GR Sport 2022", Description = "2.8L Diesel double cab pickup truck", AssetCategoryId = cats["VEH"].Id, PurchaseDate = new DateTime(2022, 11, 3), PurchaseCost = 28500000, GlCode = "5009", StateOfPurchase = "New", SerialNumber = "HILUX-GR-22001", Brand = "Toyota", ModelNumber = "Hilux GR Sport", WarrantyExpiryDate = new DateTime(2025, 11, 3), ExpectedServiceYears = 7, CurrentCondition = "Good", CurrentStatus = AssetStatus.LoanedOutExternal, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["OPS"].Id, CurrentCustodianId = staffList["FR-005"].Id, CreatedAt = new DateTime(2022, 11, 4) },
            new() { TagCode = "FRISL-2023-CMP-INV-001", AssetName = "Lenovo IdeaPad 5 Pro", Description = "Core i7, 16GB RAM, 1TB SSD", AssetCategoryId = cats["CMP"].Id, PurchaseDate = new DateTime(2023, 9, 14), PurchaseCost = 620000, GlCode = "5001", StateOfPurchase = "New", SupplierId = suppliers["TECHM"].Id, SerialNumber = "LNIDP5-INV-23001", Brand = "Lenovo", ModelNumber = "IdeaPad 5 Pro", WarrantyExpiryDate = new DateTime(2026, 9, 14), ExpectedServiceYears = 4, CurrentCondition = "Good", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["INV"].Id, CreatedAt = new DateTime(2023, 9, 15) },
            new() { TagCode = "FRISL-2019-FUR-OPS-001", AssetName = "Metal Filing Cabinet (4-drawer)", Description = "Heavy duty steel filing cabinet", AssetCategoryId = cats["FUR"].Id, PurchaseDate = new DateTime(2019, 5, 8), PurchaseCost = 75000, GlCode = "5005", StateOfPurchase = "New", SupplierId = suppliers["FURNI"].Id, SerialNumber = "MFCAB-OPS-19001", Brand = "Bisley", CurrentCondition = "Fair", CurrentStatus = AssetStatus.ActiveAssigned, CurrentLocationId = locs["HQ-ABJ"].Id, CurrentDepartmentId = depts["OPS"].Id, CreatedAt = new DateTime(2019, 5, 9) },
            new() { TagCode = "FRISL-2024-NET-IT-001", AssetName = "Ubiquiti UniFi AP AC Pro", Description = "Access point, 802.11ac wave 2, dual band", AssetCategoryId = cats["NET"].Id, PurchaseDate = new DateTime(2024, 2, 20), PurchaseCost = 185000, GlCode = "5003", StateOfPurchase = "New", SupplierId = suppliers["NETPRO"].Id, SerialNumber = "UBNT-AP-24001", Brand = "Ubiquiti", ModelNumber = "UniFi AP AC Pro", WarrantyExpiryDate = new DateTime(2026, 2, 20), ExpectedServiceYears = 5, CurrentCondition = "Good", CurrentStatus = AssetStatus.RegisteredUnassigned, CurrentLocationId = locs["STORE"].Id, CreatedAt = new DateTime(2024, 2, 21) },
            new() { TagCode = "FRISL-2020-OEQ-HR-001", AssetName = "Canon PIXMA G6020 Printer", Description = "Wireless MegaTank all-in-one printer", AssetCategoryId = cats["PRT"].Id, PurchaseDate = new DateTime(2020, 11, 12), PurchaseCost = 165000, GlCode = "5002", StateOfPurchase = "New", SerialNumber = "CNPXG6-HR-20001", Brand = "Canon", ModelNumber = "PIXMA G6020", CurrentCondition = "Poor", CurrentStatus = AssetStatus.Damaged, CurrentLocationId = locs["STORE"].Id, CurrentDepartmentId = depts["HR"].Id, CreatedAt = new DateTime(2020, 11, 13) }
        };

        db.Assets.AddRange(assets);
        db.SaveChanges();

        // RFID Tags
        int rfidSeq = 100;
        foreach (var a in assets)
        {
            db.RfidTags.Add(new RfidTag { AssetId = a.Id, RfidCode = $"RFID-{++rfidSeq:D6}", TagStatus = RfidTagStatus.Active, IsActive = true });
        }
        db.SaveChanges();

        // Status histories
        foreach (var a in assets)
        {
            if (a.CurrentStatus == AssetStatus.UnregisteredUnassigned) continue;
            db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.UnregisteredUnassigned, NewStatus = AssetStatus.RegisteredUnassigned, ChangedBy = "Admin", Reason = "Asset registered into EAMS", ChangedAt = a.CreatedAt.AddMinutes(5) });
            if (a.CurrentStatus is AssetStatus.ActiveAssigned or AssetStatus.LoanedOutExternal or AssetStatus.UnderRepair)
            {
                db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.RegisteredUnassigned, NewStatus = AssetStatus.AssignedPendingConfirmation, ChangedBy = "Admin", Reason = "Asset assigned to custodian", ChangedAt = a.CreatedAt.AddDays(2) });
                db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.AssignedPendingConfirmation, NewStatus = AssetStatus.ActiveAssigned, ChangedBy = "Staff", Reason = "Receipt confirmed by assignee", ChangedAt = a.CreatedAt.AddDays(3) });
            }
            if (a.CurrentStatus == AssetStatus.UnderRepair)
            {
                db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.ActiveAssigned, NewStatus = AssetStatus.UnderRepair, ChangedBy = "Admin", Reason = "Sent for repair – mechanical fault", ChangedAt = a.CreatedAt.AddDays(60) });
            }
            if (a.CurrentStatus == AssetStatus.Damaged)
            {
                db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.ActiveAssigned, NewStatus = AssetStatus.Damaged, ChangedBy = "Staff", Reason = "Reported as damaged during use", ChangedAt = a.CreatedAt.AddDays(90) });
            }
            if (a.CurrentStatus == AssetStatus.LoanedOutExternal)
            {
                db.AssetStatusHistories.Add(new AssetStatusHistory { AssetId = a.Id, PreviousStatus = AssetStatus.ActiveAssigned, NewStatus = AssetStatus.LoanedOutExternal, ChangedBy = "Admin", Reason = "Approved external loan for site inspection", ChangedAt = DateTime.UtcNow.AddDays(-10) });
            }
        }
        db.SaveChanges();

        // Assignments for active assets
        var activeAssets = assets.Where(a => a.CurrentStatus is AssetStatus.ActiveAssigned or AssetStatus.AssignedPendingConfirmation).ToList();
        var hqLoc = db.Locations.AsQueryable().First(l => l.Code == "HQ-ABJ");
        foreach (var a in activeAssets.Where(a => a.CurrentCustodianId.HasValue))
        {
            db.AssetAssignments.Add(new AssetAssignment
            {
                AssetId = a.Id,
                AssignedToStaffId = a.CurrentCustodianId,
                AssignedToDepartmentId = a.CurrentDepartmentId,
                AssignedLocationId = a.CurrentLocationId ?? hqLoc.Id,
                AssignedCondition = "Good",
                ConfirmedCondition = "Good",
                ConfirmationDate = a.CreatedAt.AddDays(3),
                Status = "Confirmed",
                AssignedBy = "Admin",
                AssignedDate = a.CreatedAt.AddDays(2),
                Notes = "Initial assignment on registration"
            });
        }
        // Pending assignment — Emmanuel (IT) receives Ubiquiti AP, awaiting receipt confirmation
        var pendingAsset = assets.FirstOrDefault(a => a.TagCode == "FRISL-2024-NET-IT-001");
        var emmanuelStaff = db.Staff.AsQueryable().FirstOrDefault(s => s.StaffId == "FR-009")
            ?? db.Staff.AsQueryable().FirstOrDefault(s => s.FullName == "Emmanuel");
        var itDept = db.Departments.AsQueryable().First(d => d.Code == "IT");
        if (pendingAsset != null && emmanuelStaff != null)
        {
            db.AssetAssignments.Add(new AssetAssignment
            {
                AssetId = pendingAsset.Id,
                AssignedToStaffId = emmanuelStaff.Id,
                AssignedToDepartmentId = itDept.Id,
                AssignedLocationId = hqLoc.Id,
                AssignedCondition = "Good",
                Status = "Pending",
                AssignedBy = "Admin",
                AssignedDate = DateTime.UtcNow,
                Notes = "Issued to IT — awaiting receipt confirmation"
            });
            pendingAsset.CurrentStatus = AssetStatus.AssignedPendingConfirmation;
            pendingAsset.CurrentCustodianId = emmanuelStaff.Id;
            pendingAsset.CurrentDepartmentId = itDept.Id;
            pendingAsset.CurrentLocationId = hqLoc.Id;
            pendingAsset.UpdatedAt = DateTime.UtcNow;
        }
        db.SaveChanges();

        var opsStaff = db.Staff.AsQueryable().First(s => s.StaffId == "FR-005");
        var opsDept = db.Departments.AsQueryable().First(d => d.Code == "OPS");

        // Repair request
        var printer = assets.First(a => a.TagCode.Contains("PRT-OPS"));
        db.RepairRequests.Add(new RepairRequest
        {
            AssetId = printer.Id,
            ReportedByStaffId = opsStaff.Id,
            Description = "Paper jam occurs frequently. Roller mechanism appears worn and printer does not feed paper properly.",
            Severity = "High",
            PreferredAction = "Repair",
            Status = "Pending Admin Review",
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        });

        // Asset requests
        db.AssetRequests.AddRange([
            new AssetRequest { RequestType = "New Asset", RequestedByStaffId = db.Staff.AsQueryable().First(s => s.StaffId == "FR-003").Id, DepartmentId = db.Departments.AsQueryable().First(d => d.Code == "HR").Id, Description = "Need a new laptop for the new HR officer joining next month.", Status = "Pending Department Approval", CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new AssetRequest { RequestType = "Replacement", RequestedByStaffId = opsStaff.Id, DepartmentId = opsDept.Id, Description = "Canon printer in HR is damaged beyond economic repair. Requesting replacement.", Status = "Pending Admin Approval", ApprovedByDepartmentHead = "Fatima Sule", DeptHeadApprovedAt = DateTime.UtcNow.AddDays(-2), CreatedAt = DateTime.UtcNow.AddDays(-4) }
        ]);

        // Loan + Exit Grant for vehicle
        var vehicle = assets.First(a => a.TagCode.Contains("VEH-OPS"));
        var loan = new LoanRequest
        {
            AssetId = vehicle.Id,
            RequestedByStaffId = opsStaff.Id,
            LoanType = "External",
            StartDate = DateTime.UtcNow.AddDays(-10),
            ExpectedReturnDate = DateTime.UtcNow.AddDays(5),
            Purpose = "Site inspection at Ogun State project site",
            Destination = "Sagamu, Ogun State",
            ResponsiblePerson = "Emeka Chukwu",
            Status = "Approved",
            ApprovedBy = "Admin",
            ApprovedAt = DateTime.UtcNow.AddDays(-9)
        };
        db.LoanRequests.Add(loan);
        db.SaveChanges();
        db.ExitGrants.Add(new ExitGrant { AssetId = vehicle.Id, LoanRequestId = loan.Id, GrantedBy = "Admin", GrantStartDate = loan.StartDate, GrantEndDate = loan.ExpectedReturnDate, IsActive = true, ExitReason = "Approved external loan – site inspection" });

        // RFID events
        db.RfidEvents.AddRange([
            new RfidEvent { RfidCode = "RFID-100112", EventType = "AuthorizedExitOrReturn", DoorLocation = "Main Gate", ProcessedStatus = "Authorized", AlertTriggered = false, EventTime = DateTime.UtcNow.AddHours(-2) },
            new RfidEvent { RfidCode = "RFID-UNKNOWN-X1", EventType = "ExternalAsset", DoorLocation = "Staff Entrance", ProcessedStatus = "Unknown RFID – logged as external", AlertTriggered = true, AlertMessage = "Unregistered RFID tag detected at Staff Entrance. Possible unauthorized removal.", EventTime = DateTime.UtcNow.AddHours(-5) },
            new RfidEvent { RfidCode = "RFID-100101", EventType = "AuthorizedExitOrReturn", DoorLocation = "Main Gate", ProcessedStatus = "Authorized", AlertTriggered = false, EventTime = DateTime.UtcNow.AddDays(-1) }
        ]);

        // Audit session
        db.AuditSessions.Add(new AuditSession { AuditType = "Departmental", DepartmentId = itDept.Id, InitiatedBy = "Damilola Afolabi", Status = "Open", Notes = "Annual IT asset verification", StartDate = DateTime.UtcNow.AddDays(-2) });

        // Notifications
        db.Notifications.AddRange([
            new Notification { TargetRole = "Backoffice", Title = "Warranty Expiry Alert", Message = "Cisco Catalyst 2960 Switch warranty expired 05-Sep-2024. Review recommended.", Type = "Warning", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new Notification { TargetRole = "Backoffice", Title = "Pending Repair Review", Message = "HP LaserJet Pro M428fdn has an open repair request awaiting approval.", Type = "Info", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new Notification { TargetRole = "Backoffice", Title = "External Loan Active", Message = "Toyota Hilux GR Sport is on external loan. Exit grant expires in 5 days.", Type = "Warning", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-10) },
            new Notification { TargetRole = "Backoffice", Title = "Asset Request Pending", Message = "2 asset requests are pending admin approval.", Type = "Info", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-4) },
            new Notification { TargetRole = "Auditor", Title = "Audit Scheduled", Message = "IT department annual audit is in progress. Please complete variance analysis.", Type = "Info", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        ]);

        db.SaveChanges();
    }
}
