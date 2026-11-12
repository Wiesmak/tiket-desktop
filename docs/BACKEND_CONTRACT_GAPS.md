# Backend contract gaps

Verified against upstream master d840ab12a08b06304b5e4d26568ba33e98a65ddc on 2026-11-12, unchanged from the prior reviewed baseline. Four original schemas provide eight services and 37 unary methods; no RPCs have been invented.

| Need | Actual contract | Future client approach |
| --- | --- | --- |
| Event directory | No ListRaceDays; GetRaceDay needs known UUID | Provisioned/locally known authorized IDs |
| POS-wide recent tickets | ListPlayerCoupons is per known player | Clearly partial local acknowledged IDs |
| Remote cashier print queue | GetPrintStatus takes known job ID; no listing | Bounded polling of captured job IDs |
| Edit server DRAFT | One-shot CreateRaceDay, no update method | Complete local draft, review before create |
| Player name search | No identity-directory RPC | Known UUID only |
| Paper acknowledgement | DISPATCHED means stream transfer | Operator check; explicit reprint via backend |
| Actual money disbursement | CashOut records terminal accounting | Separate operational procedure |
| Push updates | Unary methods only | Bounded reads, freshness labels |
| Cross-station unknown purchase recovery | Exact original request/key required | Future durable per-subject local journal |

Any server additions require a separate backend RFC with resource authorization, pagination, migrations and tests. Upstream is read-only for this task.
