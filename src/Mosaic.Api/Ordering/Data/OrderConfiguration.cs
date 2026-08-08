using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Data;

/// <summary>
/// How Ordering stores an order and its lines.
/// </summary>
/// <remarks>
/// <para>
/// The lines are a related entity rather than an owned one, and the reason is
/// worth knowing before you model anything with Entity Framework Core. An
/// owned type is an entity that has no key of its own, so it reaches its owner
/// as a <em>navigation</em>, and EF Core refuses to bind a navigation to a
/// constructor parameter: "only mapped properties can be bound to constructor
/// parameters". <c>OrderLine</c> is a positional record whose price is one of
/// those parameters, so owning it would mean rewriting the record.
/// </para>
/// <para>
/// As a related entity, its money becomes a <em>complex property</em> instead:
/// a value, mapped to columns on the same row, which does bind. The line has
/// no identifier in the domain model and does not need one on the graph
/// either, so the key and the foreign key back to the order are both shadow
/// properties: they exist in the database and in EF Core's model, and nowhere
/// in the C#.
/// </para>
/// </remarks>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.HasIndex(o => new { o.CustomerId, o.PlacedAt });

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// How Ordering stores one line of an order.
/// </summary>
public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        // A key nobody asked for. The line has no identity in the domain and
        // none on the graph, but a relational row needs one, so it lives as a
        // shadow property that no C# code can see or set.
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.ComplexProperty(l => l.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasPrecision(12, 2).IsRequired();
            money.Property(m => m.Currency).HasMaxLength(3).IsRequired();
        });
    }
}
