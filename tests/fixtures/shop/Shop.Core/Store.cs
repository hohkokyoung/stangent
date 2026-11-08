// Saves and reads, for which-table-a-save-writes (G3). Stand-ins for EF Core's types: the detector matches them by name.
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public EntityEntry Entry(object entity) => new();
        public void Update(object entity) { }
    }

    public class DbSet<T>
    {
        public void Add(T entity) { }
        public IEnumerable<T> Where(Func<T, bool> keep) => [];
        public IEnumerable<T> AsTracking() => [];
        public Task<int> ExecuteDeleteAsync() => Task.FromResult(0);
    }

    public class EntityEntry { public EntityState State { get; set; } }
    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }

    public static class EfExtensions
    {
        public static Task<int> ExecuteDeleteAsync<T>(this IEnumerable<T> query) => Task.FromResult(0);
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public abstract class ModelSnapshot;
}

namespace Shop.Core
{
    using Microsoft.EntityFrameworkCore;

    public class Shelf
    {
        public int Id { get; set; }
        public List<ShelfItem> Items { get; set; } = [];
    }

    // No DbSet: EF maps it because Shelf points at it.
    public class ShelfItem
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public class ShopDb : DbContext
    {
        public DbSet<Shelf> Shelves { get; set; } = new();
    }

    public class ShelfService(ShopDb db)
    {
        public void Open() => db.Shelves.Add(new Shelf());
        public void Stock(Shelf shelf) => shelf.Items.Add(new ShelfItem());
        public int Count() => db.Shelves.Where(_ => true).Count();
        // F2: the other ways EF saves.
        public void Edit(Shelf shelf) => db.Entry(shelf).State = EntityState.Modified;
        public void Replace(Shelf shelf) => db.Update(shelf);
        public Task<int> Empty() => db.Shelves.Where(_ => false).ExecuteDeleteAsync();
        public IEnumerable<Shelf> Tracked() => db.Shelves.AsTracking();
        // F3: a list on a plain object isn't a table.
        public void Plan(ShelfPlan plan) => plan.Items.Add(new ShelfItem());
    }

    public class ShelfPlan
    {
        public List<ShelfItem> Items { get; set; } = [];
    }

    // F5: a property EF is told to store as a column (JSON) isn't a navigation, so its type isn't a table.
    public class Label
    {
        public int Id { get; set; }
    }

    public class LabelledShelf
    {
        public int Id { get; set; }
        public List<Label> Labels { get; set; } = [];
    }

    public class LabelDb : DbContext
    {
        public DbSet<LabelledShelf> LabelledShelves { get; set; } = new();
        public static void Configure(PropertyBuilder<LabelledShelf> b) => b.Property(x => x.Labels);
    }

    public class PropertyBuilder<T>
    {
        public PropertyBuilder<T> Property<P>(System.Linq.Expressions.Expression<Func<T, P>> pick) => this;
    }

    // An app's own table map and generic repository (Sonarr's way).
    public class TableMapper
    {
        public TableMapper Entity<T>(string table) => this;
    }

    public static class Tables
    {
        public static void Map(TableMapper mapper) => mapper.Entity<Ledger>("Ledgers");
    }

    public class Ledger
    {
        public int Id { get; set; }
    }

    public interface IBasicRepository<T>
    {
        void Insert(T entity);
        T Get(int id);
        bool Allowed(int id);
    }

    public class LedgerService(IBasicRepository<Ledger> repository)
    {
        public void Record() => repository.Insert(new Ledger());
        public Ledger Find(int id) => repository.Get(id);
        // F10: "Allowed" isn't the word "All".
        public bool May(int id) => repository.Allowed(id);
    }
}
