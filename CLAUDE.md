# CLAUDE.md

## Vad är vroo

vroo bevakar begagnatpriser på bytbil.com. Användaren anger märke/modell i webbgränssnittet; en bakgrundstjänst skrapar alla aktiva modeller en gång per dygn (03:00 svensk tid) och sparar annonser, prisändringar och ett dagligt marknadsläge (antal till salu, medianpris). Ersätter Python-skriptet i `usedcarprices/` (behålls som referens; skrapningslogiken är portad därifrån).

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
- `Services/ScrapeService.cs` — en körning för en modell: alla resultatsidor, ny annons → hämta detaljsidan (regnr m.m.), matchar omlistade bilar på regnr, skriver `CarPrice` vid första observation och varje prisändring, och `DailyStat` när körningen lyckats.
- `Services/ScrapeScheduler.cs` — singleton/BackgroundService med en kö så att bara en skrapning körs åt gången. Dagligt schema + catch-up om senaste schemalagda körning är > 26 h gammal (t.ex. efter deploy).
- `Services/SqliteImporter.cs` — engångsimport. Gamla `price_history` lagrade *föregående* pris vid ändring; importen bygger om serien. Importerade modeller blir pausade.
- "Till salu" = bilen sågs i modellens senaste lyckade körning (`LastSeenAt >= StartedAt`).
- Inloggning: ett enda lösenord (`Auth:Password`) → JWT (30 dagar). Ingen användartabell.
- DTO:er i `Models/DTOs/`, entiteter returneras aldrig direkt.

## Konfiguration (miljövariabler i produktion)

| Nyckel | Innehåll |
|---|---|
| `DefaultConnection` | `Host=postgres;Port=5432;Database=vroo;Username=vroo;Password=…` |
| `Auth__Password` | inloggningslösenordet |
| `Jwt__Key` | `openssl rand -base64 64` |
| `Scrape__DailyAt` | valfri, default `03:00` |

## Deployment

antec, container `vroo` på `127.0.0.1:5106`, env i `/home/deploy/.env.vroo`, `vroo.forstberg.net` via Caddy. Push till `main` → runner kör `deploy.sh`.

Engångsimport i produktion:
```bash
docker cp cars.db vroo:/tmp/cars.db && docker exec vroo dotnet Vroo.dll import /tmp/cars.db
```
