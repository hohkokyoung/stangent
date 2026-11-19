using System.IO.Abstractions.TestingHelpers;
using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.Plus;
using Kavita.API.Services.Reading;
using Kavita.API.Services.SignalR;
using Kavita.Common.Helpers;
using Kavita.Database;
using Kavita.Database.Tests;
using Kavita.Models.Builders;
using Kavita.Models.DTOs.Filtering.v2;
using Kavita.Models.DTOs.Filtering.v2.Requests;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.User;
using Kavita.Services.Builders;
using Kavita.Services.Reading;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit.Abstractions;

namespace Kavita.Services.Tests;

// Planter's throwaway test (D406): a fully read series leaves Want to Read whatever its publication status.
public class PlanterWantToReadTests(ITestOutputHelper outputHelper): AbstractDbTest(outputHelper)
{
    private async Task<int> WantToReadCountAfterCleanup(PublicationStatus status)
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var readerService = new ReaderService(unitOfWork, Substitute.For<ILogger<ReaderService>>(), Substitute.For<IEventHub>(),
            Substitute.For<IImageService>(),
            new DirectoryService(Substitute.For<ILogger<DirectoryService>>(), new MockFileSystem()),
            Substitute.For<IScrobblingService>(), Substitute.For<IReadingSessionService>(),
            Substitute.For<IClientInfoAccessor>(),
            Substitute.For<IEntityNamingService>(), Substitute.For<ILocalizationService>(), Substitute.For<IBookService>());

        var s = new SeriesBuilder("Planter WTR " + status)
            .WithMetadata(new SeriesMetadataBuilder().WithPublicationStatus(status).Build())
            .WithVolume(new VolumeBuilder("1").WithChapter(new ChapterBuilder("1").WithPages(10).Build()).Build())
            .Build();
        var lib = new LibraryBuilder("Planter Lib").Build();
        s.Library = lib;
        context.Series.Add(s);

        var user = new AppUser { UserName = "planter-wtr" };
        context.AppUser.Add(user);
        await unitOfWork.CommitAsync();

        lib.AppUsers ??= new List<AppUser>();
        lib.AppUsers.Add(user);
        user.WantToRead = [new AppUserWantToRead { SeriesId = s.Id }];
        await unitOfWork.CommitAsync();

        await readerService.MarkSeriesAsRead(user, s.Id);
        await unitOfWork.CommitAsync();

        var cleanupService = new CleanupService(Substitute.For<ILogger<CleanupService>>(), unitOfWork,
            Substitute.For<IEventHub>(),
            new DirectoryService(Substitute.For<ILogger<DirectoryService>>(), new MockFileSystem()));
        await cleanupService.CleanupWantToRead();

        var wantToRead = await unitOfWork.SeriesRepository.GetWantToReadDtosForUserAsync(user.Id, new UserParams(), new SeriesFilterV2Dto());
        return wantToRead.TotalCount;
    }

    [Fact]
    public async Task FullyReadCompletedSeries_LeavesWantToRead() // control: passes at the pin
    {
        Assert.Equal(0, await WantToReadCountAfterCleanup(PublicationStatus.Completed));
    }

    [Fact]
    public async Task FullyReadOngoingSeries_LeavesWantToRead()
    {
        Assert.Equal(0, await WantToReadCountAfterCleanup(PublicationStatus.OnGoing));
    }
}
