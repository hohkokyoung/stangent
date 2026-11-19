# Static content file request

`endpoint:Sonarr.Http|GET /content/{**path:regex(^(?!api/).*)}` · backend · Sonarr.Http

From the code: GET /content/{**path:regex(^(?!api/).*)}

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the requested file under Content/ as a file response, with a content type derived from the file name, or "application/octet-stream" if unknown. [1]
- **S2** For files of type "text/plain" the response is encoded as UTF-8. [1]
- **S3** For paths starting with /MediaCoverProxy/, returns the cover image data fetched by hash, with a content type based on the file name or "application/octet-stream" if unknown. [2][3]
- **S4** Returns Not Found when the path is invalid, no mapper handles it, the mapper returns nothing, or the file is not found. [4][1]

## Actions

- **A1** A caller requests GET /content/{path}; the path is prefixed with "Content/" and resolved to a resource. [5][4] → `endpoint:Sonarr.Http|GET /content/{**path:regex(^(?!api/).*)}`

## Rules

- **R1** The endpoint allows anonymous access and applies the "AllowGet" CORS policy. [5]
- **R2** The route does not match paths beginning with "api/". [6]
- **R3** Exactly one mapper is chosen among those whose CanHandle accepts the path. [4][7]
- **R4** A path matching the invalid-path pattern returns Not Found without invoking any mapper. [4]
- **R5** When the result is a file of type "text/html", caching is disabled via Cache-Control "no-cache, no-store", Expires "-1" and Pragma "no-cache", and Last-Modified is removed. [4][8]
- **R6** A MediaCoverProxy path that does not match the expected pattern returns Not Found. [2]

## Side effects

- **E1** A warning is logged when no handler is found for the path. [4]
- **E2** A warning "File {0} not found" is logged when the mapped file does not exist. [1]

## Couldn't see

- the depth limit left out 22 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.StaticResourceMapperBase.GetResponse(Microsoft.AspNetCore.Http.HttpContext,System.String)`
2. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.MediaCoverProxyMapper.GetResponse(Microsoft.AspNetCore.Http.HttpContext,System.String)`
3. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.MediaCoverProxyMapper.CanHandle(System.String)`
4. `Sonarr.Http|M:Sonarr.Http.Frontend.StaticResourceController.MapResource(System.String)`
5. `Sonarr.Http|M:Sonarr.Http.Frontend.StaticResourceController.IndexContent(System.String)`
6. `endpoint:Sonarr.Http|GET /content/{**path:regex(^(?!api/).*)}`
7. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.IMapHttpRequestsToDisk.CanHandle(System.String)`
8. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.DisableCache(Microsoft.AspNetCore.Http.IHeaderDictionary)`

<sub>commit 0bffefe · inputs cbffdf2 · slice of 14 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
