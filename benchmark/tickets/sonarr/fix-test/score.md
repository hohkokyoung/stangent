# D345 fix test: does the bundle help a fix?

**Bar: failed.** B 9 clean and green vs A 9, tokens 127% of A's: the fixer gets only the finder's files and the failing test (D345).

| side | runs | green | clean | symptom | unjudged | touches a fix file | tokens | cost |
|---|---|---|---|---|---|---|---|---|
| A · ticket | 12 | 12 | 9 | 3 | 0 | 12 | 674,159 | $0.70 |
| B · bundle | 12 | 12 | 9 | 3 | 0 | 12 | 854,033 | $0.92 |

| bug | A | B |
|---|---|---|
| SONB-12 | fixed · clean · 1/1 files · 44,383 tok | fixed · clean · 1/1 files · 53,830 tok |
| SONB-13 | fixed · clean · 1/1 files · 65,522 tok | fixed · clean · 1/1 files · 72,672 tok |
| SONB-15 | fixed · symptom · 1/2 files · 50,416 tok | fixed · symptom · 1/2 files · 43,689 tok |
| SONB-19 | fixed · clean · 1/1 files · 46,386 tok | fixed · clean · 1/1 files · 53,433 tok |
| SONB-20 | fixed · clean · 1/1 files · 26,688 tok | fixed · clean · 1/1 files · 48,075 tok |
| SONB-21 | fixed · clean · 1/2 files · 58,084 tok | fixed · clean · 1/2 files · 94,946 tok |
| SONB-22 | fixed · clean · 1/1 files · 81,123 tok | fixed · clean · 1/1 files · 100,668 tok |
| SONB-23 | fixed · symptom · 1/2 files · 53,408 tok | fixed · symptom · 1/2 files · 63,407 tok |
| SONB-24 | fixed · symptom · 1/3 files · 68,638 tok | fixed · symptom · 1/3 files · 120,036 tok |
| SONB-25 | fixed · clean · 1/2 files · 85,588 tok | fixed · clean · 1/2 files · 90,560 tok |
| SONB-26 | fixed · clean · 1/3 files · 46,528 tok | fixed · clean · 1/3 files · 59,662 tok |
| SONB-27 | fixed · clean · 1/1 files · 47,395 tok | fixed · clean · 1/1 files · 53,055 tok |
