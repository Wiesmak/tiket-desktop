# Executive demonstration workflow

Use the managed disposable lab profile and normal browser sign-in as the lab event administrator.
Administration → Create new lab event / resume preparation retains one event intent and 24 player identities across restart.
The native builder saves the six-race template; review/create, enroll the roster, then publish and start.
For each selected race: open betting, quote/resume the guided round, review/place, close, submit ordered finishers, verify and settle.
Finalize and complete after all races settle. Event Results & Payouts builds authoritative settlement/account detail and exports the displayed rows through a native CSV picker.
Aliases are local labels keyed by profile/event/player UUID. They are neither an identity directory nor authorization.
Rounds retain immutable coupon/key/command IDs and exact server quotes in protected local storage. Pending or UNKNOWN commands stop the round and require explicit original-request recovery in existing transaction controls.

The optional `Tiket.Lab.Provisioning` executable listens only on `127.0.0.1:9091` and targets only the disposable `https://localhost:8443/realms/tiket-lab` issuer.
Its service environment requires `TIKET_LAB_ADMIN_USER` and `TIKET_LAB_ADMIN_PASSWORD`; credentials are never included in the desktop, source, profiles, or reports.
Start it with `dotnet run --project Tiket.Lab.Provisioning` in the configured lab environment. This is a lab prerequisite, not a presentation-time operator action.
The capability validates RS256 signature, issuer, audience, expiry, public-client binding and EVENT_ADMIN role, then unions explicit event/player UUID grants only for the authenticated operator.
The desktop exposes it only for the exact lab issuer, loopback backend and DevelopmentLoopback profile. It obtains updated grants through normal OIDC token reissue and discovers signed event selectors.
No backend financial authorization checks, contracts, volumes or historical journals are changed. The service does not create events, enroll players, place bets or settle races.
All those operations continue through authenticated gRPC and existing per-command/per-coupon journals.
