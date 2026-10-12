using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace AdminPanel.API.Migrations;

public partial class HardenDataIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing duplicates or overlong values must be resolved explicitly before applying this migration.
        migrationBuilder.Sql(@"
IF EXISTS (SELECT Username FROM Users GROUP BY Username HAVING COUNT(*) > 1)
    THROW 51000, 'Resolve duplicate usernames before applying HardenDataIntegrity.', 1;
IF EXISTS (SELECT Slug FROM ContentPages GROUP BY Slug HAVING COUNT(*) > 1)
    THROW 51000, 'Resolve duplicate content slugs before applying HardenDataIntegrity.', 1;
IF EXISTS (SELECT 1 FROM Users WHERE DATALENGTH(Username) > 512)
    THROW 51000, 'Resolve usernames longer than 256 characters before applying HardenDataIntegrity.', 1;
IF EXISTS (SELECT 1 FROM ContentPages WHERE DATALENGTH(Slug) > 400)
    THROW 51000, 'Resolve slugs longer than 200 characters before applying HardenDataIntegrity.', 1;");

        ChangeForeignKeys(migrationBuilder, ReferentialAction.Restrict);
        migrationBuilder.AlterColumn<string>("Username", "Users", type: "nvarchar(256)", maxLength: 256, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(max)");
        migrationBuilder.AlterColumn<string>("Slug", "ContentPages", type: "nvarchar(200)", maxLength: 200, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(max)");
        migrationBuilder.CreateIndex("IX_Users_Username", "Users", "Username", unique: true);
        migrationBuilder.CreateIndex("IX_ContentPages_Slug", "ContentPages", "Slug", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Users_Username", "Users");
        migrationBuilder.DropIndex("IX_ContentPages_Slug", "ContentPages");
        migrationBuilder.AlterColumn<string>("Username", "Users", type: "nvarchar(max)", nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(256)", oldMaxLength: 256);
        migrationBuilder.AlterColumn<string>("Slug", "ContentPages", type: "nvarchar(max)", nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200);
        ChangeForeignKeys(migrationBuilder, ReferentialAction.Cascade);
    }

    private static void ChangeForeignKeys(MigrationBuilder migrationBuilder, ReferentialAction action)
    {
        foreach (var (table, column, principal) in new[]
        {
            ("Products", "CategoryId", "Categories"), ("OrderItems", "ProductId", "Products"),
            ("Orders", "CustomerId", "Users"), ("Discounts", "ProductId", "Products")
        })
        {
            var name = $"FK_{table}_{principal}_{column}";
            migrationBuilder.DropForeignKey(name, table);
            migrationBuilder.AddForeignKey(name, table, column, principal, principalColumn: "Id", onDelete: action);
        }
    }
}
