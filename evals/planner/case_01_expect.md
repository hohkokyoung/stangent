# Expected output checks for planner case_01
# Lines starting with # are comments
# Lines starting with ! are negations (response must NOT contain these)
# All other lines: response must contain this phrase (case-insensitive)

# Must produce a scope section
## Scope

# Must produce acceptance criteria
## Acceptance Criteria

# Must produce an out of bounds section
## Out of Bounds

# Must reference the ADR
ADR-001

# Must reference Riverpod (the ADR mandates it)
Riverpod

# Must not ask more than 5 questions if it asks any
# (checked by assert module)

# Must include at least one AC about valid credentials
valid

# Must include at least one AC about invalid credentials or error
error

# Must include files to touch
## Files to Touch

# Must NOT implement code
!```dart
!```python

# Must NOT ask about which state management to use (ADR already decided)
!which state management
!what state management
!Provider or Riverpod
