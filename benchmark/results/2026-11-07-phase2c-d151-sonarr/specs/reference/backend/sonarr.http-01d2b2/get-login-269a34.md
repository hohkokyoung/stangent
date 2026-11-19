# Login page (GET /login)

`endpoint:Sonarr.Http|GET /login` · backend · Sonarr.Http

From the code: GET /login

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the static login page resource for the path "/login" as a file response. [1][2]
- **S2** Returns a Not Found response when no handler can serve the path, when the handler returns nothing, or when the file is not found on disk. [2][3]
- **S3** The file's content type is determined from the file path, and defaults to application/octet-stream when unknown. For text/plain, UTF-8 encoding is set. [3]

## Actions

- **A1** A caller requests GET /login and receives the login page resource. [1] → `endpoint:Sonarr.Http|GET /login`

## Rules

- **R1** The login page is accessible anonymously, without authentication. [1]
- **R2** A path matching the invalid-path pattern returns Not Found before any handler is tried. [2]
- **R3** Exactly one registered mapper must claim the path via CanHandle; if none does, Not Found is returned and a warning is logged. [2][4]
- **R4** The media cover proxy mapper handles only paths starting with "/MediaCoverProxy/" (case-insensitive). [5]

## Side effects

- **E1** When the response is an HTML file, cache is disabled: Last-Modified is removed, Cache-Control is "no-cache, no-store", Expires is "-1" and Pragma is "no-cache". [2][6]
- **E2** A warning is logged when no handler exists for the path or the file is not found. [2][3]

## Couldn't see

- the depth limit left out 22 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.Frontend.StaticResourceController.LoginPage`
2. `Sonarr.Http|M:Sonarr.Http.Frontend.StaticResourceController.MapResource(System.String)`
3. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.StaticResourceMapperBase.GetResponse(Microsoft.AspNetCore.Http.HttpContext,System.String)`
4. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.IMapHttpRequestsToDisk.CanHandle(System.String)`
5. `Sonarr.Http|M:Sonarr.Http.Frontend.Mappers.MediaCoverProxyMapper.CanHandle(System.String)`
6. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.DisableCache(Microsoft.AspNetCore.Http.IHeaderDictionary)`

<sub>commit 0bffefe · inputs 5935b2a · slice of 14 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
