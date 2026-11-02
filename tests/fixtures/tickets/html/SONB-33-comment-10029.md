_halfskye wrote:_

It appears that the "Recommended Shows Of All Time" option is currently implemented to use the "yearly" period in its request link generation too so it seems likely this would result in the same weekly recommendations instead:
https://github.com/Sonarr/Sonarr/blob/94f64435f558acd9c3e476eb55909998e1290e02/src/NzbDrone.Core/ImportLists/Trakt/Popular/TraktPopularRequestGenerator.cs#L55-L59
