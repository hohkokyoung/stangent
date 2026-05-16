# Expected output checks for planner case_02 — ADR contradiction detection

# Must detect the ADR-002 conflict before writing the spec
ADR-002

# Must flag the conflict with http package vs ApiClient
!http package

# Must mention ApiClient as the required approach
ApiClient

# Must offer options: comply, override, or cancel
A
B
C

# Must NOT silently proceed and write a spec using http package
!## Scope

# Must NOT ask about which HTTP client to use as if it were open
!which http
!what http client
!http or dio
