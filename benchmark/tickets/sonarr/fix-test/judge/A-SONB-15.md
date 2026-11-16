# SONB-15: Cannot add "Unknown" resolution to a custom format

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs b/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
index 1eb90b9532..02e3f4cb2d 100644
--- a/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
+++ b/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
@@ -1,3 +1,4 @@
+using System;
 using FluentValidation;
 using NzbDrone.Core.Annotations;
 using NzbDrone.Core.Parser;
@@ -9,13 +10,19 @@ public class ResolutionSpecificationValidator : AbstractValidator<ResolutionSpec
     {
         public ResolutionSpecificationValidator()
         {
-            RuleFor(c => c.Value).NotEmpty();
+            RuleFor(c => c.Value).Custom((value, context) =>
+            {
+                if (!Enum.IsDefined(typeof(Resolution), value))
+                {
+                    context.AddFailure($"Invalid resolution condition value: {value}");
+                }
+            });
         }
     }
 
     public class ResolutionSpecification : CustomFormatSpecificationBase
     {
-        private static readonly ResolutionSpecificationValidator Validator = new ResolutionSpecificationValidator();
+        private static readonly ResolutionSpecificationValidator Validator = new();
 
         public override int Order => 6;
         public override string ImplementationName => "Resolution";
diff --git a/src/NzbDrone.Core/CustomFormats/Specifications/SourceSpecification.cs b/src/NzbDrone.Core/CustomFormats/Specifications/SourceSpecification.cs
index 06c78e1ff3..b7364d30e3 100644
--- a/src/NzbDrone.Core/CustomFormats/Specifications/SourceSpecification.cs
+++ b/src/NzbDrone.Core/CustomFormats/Specifications/SourceSpecification.cs
@@ -1,3 +1,4 @@
+using System;
 using FluentValidation;
 using NzbDrone.Core.Annotations;
 using NzbDrone.Core.Qualities;
@@ -9,13 +10,19 @@ public class SourceSpecificationValidator : AbstractValidator<SourceSpecificatio
     {
         public SourceSpecificationValidator()
         {
-            RuleFor(c => c.Value).NotEmpty();
+            RuleFor(c => c.Value).Custom((value, context) =>
+            {
+                if (!Enum.IsDefined(typeof(QualitySource), value))
+                {
+                    context.AddFailure($"Invalid source condition value: {value}");
+                }
+            });
         }
     }
 
     public class SourceSpecification : CustomFormatSpecificationBase
     {
-        private static readonly SourceSpecificationValidator Validator = new SourceSpecificationValidator();
+        private static readonly SourceSpecificationValidator Validator = new();
 
         public override int Order => 5;
         public override string ImplementationName => "Source";

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs b/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
index 1eb90b9532..a50c93af2b 100644
--- a/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
+++ b/src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs
@@ -9,7 +9,7 @@ public class ResolutionSpecificationValidator : AbstractValidator<ResolutionSpec
     {
         public ResolutionSpecificationValidator()
         {
-            RuleFor(c => c.Value).NotEmpty();
+            RuleFor(c => c.Value).Must(v => System.Enum.IsDefined(typeof(Resolution), v));
         }
     }
 

```
