# Outsource Request App

Internal ASP.NET Core MVC portal for submitting and approving outsourcing requests at Rittal-CSM.

## Approval chain

1. **Work Preparation** (WP) — sign-off  
2. **Production** (PROD) — sign-off  
3. **Cost Compact / Strategic Buyer** (BUYER) — PPAP + cost impact  
4. **Sourcing & Procurement** (SOURCING) — sign-off  
5. **Managing Director** (MD) — final authorisation  

## Local development

```bash
dotnet restore
dotnet run
```

Set `DevImpersonateUser` in `appsettings.Development.json` to an email that matches an Approver role or Admin user so you can exercise the workflow without Windows Auth.

Bootstrap the first admin (only works when no admins are configured yet):

```
/Admin/Seed?username=you@company.com
```

## Configuration

| Setting | Where |
|---|---|
| SQL Server connection strings | `appsettings.json` → `ConnectionStrings` |
| Public portal URL (used in emails) | `appsettings.json` → `BaseAddress` |
| SMTP host/port/from, reminder hours, admin list, approver roles | Admin panel at `/Admin` |

Apply pending EF migrations against the app database before first run in a new environment:

```bash
dotnet ef database update --context AppDbContext
```

## IIS deployment

`web.config` enables Windows Authentication (anonymous disabled) for in-process hosting. Approver/admin matching uses the email configured in Admin — if Windows Auth supplies `DOMAIN\user`, either store that value in the role Email field or map identities to email addresses at the reverse-proxy / SSO layer.
