using System.Linq;
using System.Threading.Tasks;
using Kavita.Common.Helpers;
using Kavita.Models.Builders;
using Kavita.Services.Builders;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Database.Tests.Repositories;

// Planter's throwaway test (D406): Browse Genres must not list a genre no series uses.
public class PlanterGenreBrowseTests(ITestOutputHelper outputHelper): AbstractDbTest(outputHelper)
{
    [Fact]
    public async Task BrowseGenres_DoesNotListAGenreNoSeriesUses()
    {
        var (unitOfWork, context, _) = await CreateDatabase();

        var used = new GenreBuilder("Used Genre").Build();
        var unused = new GenreBuilder("Unused Genre").Build();
        context.Genre.AddRange(used, unused);
        await context.SaveChangesAsync();

        var user = new AppUserBuilder("reader", "reader@example.com").Build();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var lib = new LibraryBuilder("lib")
            .WithSeries(new SeriesBuilder("s0")
                .WithMetadata(new SeriesMetadataBuilder().WithGenres([used]).Build())
                .WithVolume(new VolumeBuilder("1").WithChapter(new ChapterBuilder("1").Build()).Build())
                .Build())
            .Build();
        context.Library.Add(lib);
        await context.SaveChangesAsync();

        // The reader can see every library on the server (as on a typical home server).
        foreach (var library in context.Library.ToList()) user.Libraries.Add(library);
        await context.SaveChangesAsync();

        var genres = await unitOfWork.GenreRepository.GetBrowseableGenre(user.Id, new UserParams());

        Assert.Contains(genres, g => g.Id == used.Id);
        Assert.DoesNotContain(genres, g => g.Id == unused.Id);
    }
}
