// EF's own list of tables (D164), in its own project: a stand-in for a migrations snapshot.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Shop.Core
{
}

namespace Shop.Web.Data
{
    public class Coupon
    {
        public int Id { get; set; }
        public List<CouponNote> Notes { get; set; } = [];
    }

    // Reached through a navigation, but the snapshot doesn't hold it: not a table.
    public class CouponNote
    {
        public int Id { get; set; }
    }

    public class WebDb : DbContext
    {
        public DbSet<Coupon> Coupons { get; set; } = new();
    }

    public static class EntityBuilders
    {
        public static void Entity(this ModelBuilder builder, string name, Action<EntityBuilder> build) { }
        public static void ToTable(this EntityBuilder builder, string name) { }
        public static void OwnsOne(this EntityBuilder builder, string type, string property, Action<EntityBuilder> build) { }
    }

    public class ModelBuilder;
    public class EntityBuilder;

    public class WebDbModelSnapshot : ModelSnapshot
    {
        public static void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity("Shop.Web.Data.Coupon", b =>
            {
                b.ToTable("CouponCodes");
                b.OwnsOne("Shop.Web.Data.Owned", "Owned", b1 => b1.ToTable("NotATable"));
            });
            modelBuilder.Entity("CouponShelf", b => b.ToTable("CouponShelf"));
        }
    }
}
