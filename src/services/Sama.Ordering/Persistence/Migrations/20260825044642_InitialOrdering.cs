using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sama.Ordering.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ordering");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipping_line1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    shipping_city = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    shipping_postcode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    shipping_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => new { x.order_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                schema: "ordering",
                table: "orders",
                columns: new[] { "tenant_id", "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status",
                schema: "ordering",
                table: "orders",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_number",
                schema: "ordering",
                table: "orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "ordering",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "occurred_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "ordering");
        }
    }
}
