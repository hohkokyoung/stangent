# SONB-25: Sonarr does not recognize multi audio English / French

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core.Test/ParserTests/LanguageParserFixture.cs b/src/NzbDrone.Core.Test/ParserTests/LanguageParserFixture.cs
index b9c69d7e91..64470a49c1 100644
--- a/src/NzbDrone.Core.Test/ParserTests/LanguageParserFixture.cs
+++ b/src/NzbDrone.Core.Test/ParserTests/LanguageParserFixture.cs
@@ -86,6 +86,8 @@ public void should_parse_language_french(string postTitle)
         [TestCase("Series Title S01 1080p Eng Fra [mkvonly]")]
         [TestCase("Series Title S01 Eng Fre Multi Subs 720p [H264 mp4]")]
         [TestCase("Series-Title-S01-[DVDRip]-H264-Fra-Ac3-2-0-Eng-5-1")]
+        [TestCase("Series Title S01 1080p FR ENG [mkvonly]")]
+        [TestCase("Series Title S01 1080p ENG FR [mkvonly]")]
         public void should_parse_language_french_english(string postTitle)
         {
             var result = LanguageParser.ParseLanguages(postTitle);
diff --git a/src/NzbDrone.Core/Parser/LanguageParser.cs b/src/NzbDrone.Core/Parser/LanguageParser.cs
index cbcaf5a222..65a620646c 100644
--- a/src/NzbDrone.Core/Parser/LanguageParser.cs
+++ b/src/NzbDrone.Core/Parser/LanguageParser.cs
@@ -20,7 +20,7 @@ public static class LanguageParser
                 new RegexReplace(@".*?[_. ](S\d{2}(?:E\d{2,4})*[_. ].*)", "$1", RegexOptions.Compiled | RegexOptions.IgnoreCase)
             };
 
-        private static readonly Regex LanguageRegex = new Regex(@"(?:\W|_)(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:\W|_|\b)(?:FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?:\W|_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕)|(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan|catalán)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<japanese>\(JA\))|(?<original>\b(?:orig|original)\b)",
+        private static readonly Regex LanguageRegex = new Regex(@"(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:\W|_|\b)(?:FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?:\W|_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕)|(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan|catalán)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<japanese>\(JA\))|(?<original>\b(?:orig|original)\b)",
                                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
 
         private static readonly Regex CaseSensitiveLanguageRegex = new Regex(@"(?:(?i)(?<!SUB[\W|_|^]))(?:(?<lithuanian>\bLT\b)|(?<czech>\bCZ\b)|(?<polish>\bPL\b)|(?<bulgarian>\bBG\b)|(?<slovak>\bSK\b)|(?<german>\bDE\b))(?:(?i)(?![\W|_|^]SUB))",

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Parser/LanguageParser.cs b/src/NzbDrone.Core/Parser/LanguageParser.cs
index cbcaf5a222..72ff295cad 100644
--- a/src/NzbDrone.Core/Parser/LanguageParser.cs
+++ b/src/NzbDrone.Core/Parser/LanguageParser.cs
@@ -20,7 +20,7 @@ public static class LanguageParser
                 new RegexReplace(@".*?[_. ](S\d{2}(?:E\d{2,4})*[_. ].*)", "$1", RegexOptions.Compiled | RegexOptions.IgnoreCase)
             };
 
-        private static readonly Regex LanguageRegex = new Regex(@"(?:\W|_)(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:\W|_|\b)(?:FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?:\W|_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕)|(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan|catalán)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<japanese>\(JA\))|(?<original>\b(?:orig|original)\b)",
+        private static readonly Regex LanguageRegex = new Regex(@"(?:\W|_)(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:\W|_|\b)(?:FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?=\W|_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕)|(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan|catalán)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<japanese>\(JA\))|(?<original>\b(?:orig|original)\b)",
                                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
 
         private static readonly Regex CaseSensitiveLanguageRegex = new Regex(@"(?:(?i)(?<!SUB[\W|_|^]))(?:(?<lithuanian>\bLT\b)|(?<czech>\bCZ\b)|(?<polish>\bPL\b)|(?<bulgarian>\bBG\b)|(?<slovak>\bSK\b)|(?<german>\bDE\b))(?:(?i)(?![\W|_|^]SUB))",

```
