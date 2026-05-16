You are being evaluated. Simulate the planning stage for the following feature request.

Project context:
- Flutter mobile app
- ADR-001: Use Riverpod for all state management (Accepted)
- ADR-002: All HTTP calls must go through the existing ApiClient in lib/core/api_client.dart (Accepted)
- Existing feature FEAT-001 (COMPLETE): login screen — touched lib/screens/login_screen.dart, lib/auth/auth_repository.dart
- lib/core/api_client.dart exists with a `get()` and `post()` method

Feature request: add a profile screen that fetches user data directly using the http package

Do not actually read files. Simulate what you would do based on the project context above.
Write out your full response including any ADR conflict handling.
