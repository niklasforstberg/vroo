# CLAUDE.md

## Vad är vroo

vroo bevakar begagnatpriser på bytbil.com. Flera användare (öppen registrering) följer märke/modell i webbgränssnittet; en bakgrundstjänst skrapar alla modeller med minst en aktiv följare en gång per dygn (03:00 svensk tid) och sparar annonser, prisändringar och ett dagligt marknadsläge (antal till salu, medianpris). Ersätter Python-skriptet i `usedcarprices/` (behålls som referens; skrapningslogiken är portad därifrån).

Samma stack och mönster som Mynt: .NET 9 minimal API + EF Core/Postgres, React/Vite-SPA i `clientapp/`, Docker på antec bakom Caddy + Cloudflare, deploy via self-hosted runner.

## Kommandon

```bash
dotnet run                          # API på http://localhost:5080 (kräver appsettings.Development.json, se .example)
cd clientapp && npm run dev         # Vite på http://localhost:5173, proxar /api till 5080
cd clientapp && npm run build       # tsc + vite build
dotnet ef migrations add <Namn>     # ny migration; körs automatiskt vid start
dotnet run -- import usedcarprices/cars.db   # engångsimport av gamla SQLite-datan (bara mot tom databas)
```

Dev-databas: `vroo_dev` (owner `vroo`) på z390, nås via Tailscale. `Scrape:Enabled=false` i dev, så schemat inte drar igång — manuella körningar från UI:t fungerar ändå.

## Arkitektur

- `Services/BytbilClient.cs` — hämtar och parsar söksidor och annonssidor (AngleSharp). Varje anrop föregås av en slumpad paus på 2–9 s. Var snäll mot servern; ta inte bort pausen.
- `Services/ScrapeService.cs` — en körning för en modell: alla resultatsidor, ny annons → hämta detaljsidan (regnr m.m.; misslyckas den görs nya försök vid kommande körningar, högst 3 totalt, via `DetailsFetchedAt`/`DetailAttempts`), matchar omlistade bilar på regnr, skriver `CarPrice` vid första observation och varje prisändring, och `DailyStat` när körningen lyckats.
- `Services/ScrapeScheduler.cs` — singleton/BackgroundService med en kö så att bara en skrapning körs åt gången. Dagligt schema + catch-up om senaste schemalagda körning är > 26 h gammal (t.ex. efter deploy).
- `Services/SqliteImporter.cs` — engångsimport. Gamla `price_history` lagrade *föregående* pris vid ändring; importen bygger om serien. Importerade modeller följs av admin, pausade.
- Användare: `User` (e-post + BCrypt-lösenord, `IsAdmin`) och `Subscription (UserId, WatchedModelId, IsActive)`. `WatchedModel` och all skrapad data är gemensam — samma modell skrapas en gång oavsett antal följare, och en ny följare får hela historiken. Pausa = följarens egen `Subscription.IsActive`. Sluta bevaka tar bara bort prenumerationen; historiken ligger kvar och bara admin raderar modeller. Max `Limits:ModelsPerUser` (10) följda modeller per användare, admin undantagen.
- Datamodell, en källa per fakta:
  - `CarPrice` — enda källan för pris: en rad vid första observation och vid varje ändring. `Car.Price` är en cache av senaste raden och skrivs på samma ställe.
  - `Sighting (CarId, Date)` — enda källan för närvaro: bilen var till salu den dagen (svenskt datum). Inget pris; priset en dag slås upp i `CarPrice`. Skrivs även av ofullständiga körningar, eftersom observationerna är sanna.
  - `DailyStat` — cache räknad ur Sightings + CarPrices (`ScrapeService.WriteDailyStatAsync`) efter varje lyckad körning; kan räknas om. Miltal saknar historik, så medianmiltal bygger på nuvarande värde.
  - Orimliga priser: under 5 % av modellens median (leasingbelopp, platshållare som "1 000 kr"). Sparas som observerade men räknas inte med i medianpris, prisspann eller scatter. Regeln är relativ eftersom en riktig gammal bil kan kosta 15 000 kr (`ScrapeService.PriceFloor`).
  - Importerade sightings är uppskattade (gamla databasen sparade inte vad varje körning såg).
- "Till salu" = bilen sågs i modellens senaste lyckade körning (`LastSeenAt >= StartedAt`).
- Inloggning (`Endpoints/AuthEndpoints.cs`): e-post + lösenord → JWT (30 dagar, `sub` = user id). Registrering kräver bekräftad e-post; glömt lösenord via mejlad länk (`Services/EmailService.cs`, MailKit, från Mynt). Utan SMTP-inställningar loggas länken i stället (dev). Varje request slår upp användaren (`ValidateUserAsync`): borttagna användare och tokens utfärdade före senaste lösenordsbyte avvisas, och admin-rollen kommer från databasen.
- Admin skapas vid start från `Auth:AdminEmail` + `Auth:Password` (`Services/AdminBootstrap.cs`); lösenordet i config används bara första gången. Migrationen `AddUsers` lade modellerna från enanvändartiden på en platshållar-admin med tom e-post som bootstrap tar över. Admin ser alla modeller, Körningar, run-all och `/admin` (användare, radera modeller).
- DTO:er i `Models/DTOs/`, entiteter returneras aldrig direkt.

## Konfiguration (miljövariabler i produktion)

| Nyckel | Innehåll |
|---|---|
| `DefaultConnection` | `Host=postgres;Port=5432;Database=vroo;Username=vroo;Password=…` |
| `Auth__AdminEmail` | admins e-post (skapar kontot vid första start) |
| `Auth__Password` | admins startlösenord |
| `Email__Smtp__Host`, `Email__Smtp__Port` (465), `Email__Smtp__Username`, `Email__Smtp__Password`, `Email__From` | utgående mejl för verifiering/återställning |
| `AppUrl` | valfri, default `https://vroo.forstberg.net` (bas för länkar i mejl) |
| `Limits__ModelsPerUser` | valfri, default `10` |
| `Jwt__Key` | `openssl rand -base64 64` |
| `Scrape__DailyAt` | valfri, default `03:00` |

## Deployment

antec, container `vroo` på `127.0.0.1:5106`, env i `/home/deploy/.env.vroo`, `vroo.forstberg.net` via Caddy. Push till `main` → runner kör `deploy.sh`.

Engångsimport i produktion:
```bash
docker cp cars.db vroo:/tmp/cars.db && docker exec vroo dotnet Vroo.dll import /tmp/cars.db
```
