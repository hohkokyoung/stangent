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
        public List<CouponTag> Tags { get; set; } = [];
    }

    // Many-to-many with Coupon (R2): an add writes the join table the snapshot names.
    public class CouponTag
    {
        public int Id { get; set; }
        public List<Coupon> Coupons { get; set; } = [];
    }

    public class CouponService
    {
        public void Tag(Coupon coupon) => coupon.Tags.Add(new CouponTag());
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
        public static EntityBuilder HasOne(this EntityBuilder builder, string type, string? navigation) => builder;
    }

    public class ModelBuilder;
    public class EntityBuilder;

    [DbContext(typeof(WebDb))]
    public class WebDbModelSnapshot : ModelSnapshot
    {
        public static void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity("Shop.Web.Data.Coupon", b =>
            {
                b.ToTable("CouponCodes");
                b.OwnsOne("Shop.Web.Data.Owned", "Owned", b1 => b1.ToTable("NotATable"));
            });
            modelBuilder.Entity("Shop.Web.Data.CouponTag", b => b.ToTable("CouponTags"));
            modelBuilder.Entity("CouponShelf", b => b.ToTable("CouponShelf"));
            modelBuilder.Entity("CouponCouponTag", b => b.ToTable("CouponCouponTag"));
            modelBuilder.Entity("CouponCouponTag", b =>
            {
                b.HasOne("Shop.Web.Data.Coupon", null);
                b.HasOne("Shop.Web.Data.CouponTag", null);
            });
        }
    }

    // R3: the snapshot of a context in Shop.Core, kept here the way a migrations project keeps it.
    [DbContext(typeof(Shop.Core.ArchiveDb))]
    public class ArchiveDbModelSnapshot : ModelSnapshot
    {
        public static void BuildModel(ModelBuilder modelBuilder) => modelBuilder.Entity("Shop.Core.Archive", b => b.ToTable("Archives"));
    }
}
