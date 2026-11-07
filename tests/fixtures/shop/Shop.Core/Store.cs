// Saves and reads, for which-table-a-save-writes (G3). Stand-ins for EF Core's types: the detector matches them by name.
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext;

    public class DbSet<T>
    {
        public void Add(T entity) { }
        public IEnumerable<T> Where(Func<T, bool> keep) => [];
    }
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
    }

    public class LedgerService(IBasicRepository<Ledger> repository)
    {
        public void Record() => repository.Insert(new Ledger());
        public Ledger Find(int id) => repository.Get(id);
    }
}
