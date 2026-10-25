-- Two series with seasons and episodes, inserted directly so no metadata service is needed (D76).
-- Only columns Sonarr has required for years, so the same seed works at every commit the proofs run on.
INSERT INTO RootFolders (Path) VALUES ('@DATA@/tv');
INSERT INTO Series (TvdbId, TvRageId, TvMazeId, Title, TitleSlug, CleanTitle, SortTitle, Status, Overview, Images, Path, Monitored, SeasonFolder,
  Runtime, SeriesType, UseSceneNumbering, Year, Seasons, Genres, Actors, Tags, QualityProfileId, Added, OriginalLanguage, Network, FirstAired)
VALUES
 (900001, 0, 0, 'The Test Show', 'the-test-show', 'thetestshow', 'test show', 0, 'A seeded series for stangent''s Playwright reproductions.', '[]',
  '@DATA@/tv/The Test Show', 1, 1, 30, 0, 0, 2020, '[{"seasonNumber":1,"monitored":true},{"seasonNumber":2,"monitored":true}]', '[]', '[]', '[]', 1,
  '2026-01-01 00:00:00Z', 1, 'Test Network', '2020-01-06 00:00:00Z'),
 (900002, 0, 0, 'Second Show', 'second-show', 'secondshow', 'second show', 1, 'An ended series, for lists and filters.', '[]',
  '@DATA@/tv/Second Show', 0, 1, 45, 0, 0, 2015, '[{"seasonNumber":1,"monitored":false}]', '[]', '[]', '[]', 1,
  '2026-01-02 00:00:00Z', 1, 'Other Network', '2015-03-02 00:00:00Z');
INSERT INTO Episodes (SeriesId, SeasonNumber, EpisodeNumber, Title, Overview, Monitored, AirDate, AirDateUtc, UnverifiedSceneNumbering, Runtime)
VALUES
 (1, 1, 1, 'Pilot', 'The first episode.', 1, '2020-01-06', '2020-01-06 20:00:00Z', 0, 30),
 (1, 1, 2, 'The Second One', 'The second episode.', 1, '2020-01-13', '2020-01-13 20:00:00Z', 0, 30),
 (1, 1, 3, 'Third Time', 'The third episode.', 1, '2020-01-20', '2020-01-20 20:00:00Z', 0, 30),
 (1, 2, 1, 'Season Two Opener', 'Back again.', 1, '2021-01-04', '2021-01-04 20:00:00Z', 0, 30),
 (1, 2, 2, 'Midseason', 'More of it.', 0, '2021-01-11', '2021-01-11 20:00:00Z', 0, 30),
 (2, 1, 1, 'Beginning', 'It starts.', 0, '2015-03-02', '2015-03-02 21:00:00Z', 0, 45),
 (2, 1, 2, 'Ending', 'It ends.', 0, '2015-03-09', '2015-03-09 21:00:00Z', 0, 45);
