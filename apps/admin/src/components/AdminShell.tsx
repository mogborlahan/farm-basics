import { useQuery } from "@tanstack/react-query";
import { fetchCurrentIdentity, fetchHealthReady } from "../api/client";
import { useAuth } from "../auth/AuthProvider";

/**
 * Thirteen canonical Admin & Operations consoles per Part B §2 and D-23.
 */
const CANONICAL_CONSOLES = [
  { id: "master-data", label: "Master Data Governance" },
  { id: "identity-trust", label: "Identity & Org Trust" },
  { id: "review-ops", label: "Review Operations" },
  { id: "vendor-partner", label: "Vendor & Partner" },
  { id: "marketplace-ops", label: "Marketplace Operations" },
  { id: "field-ops", label: "Field Operations" },
  { id: "finance", label: "Finance & Escrow" },
  { id: "content-gov", label: "Content Governance" },
  { id: "land-cert", label: "Land & Certifications" },
  { id: "trust-safety", label: "Trust & Safety" },
  { id: "analytics", label: "Analytics" },
  { id: "config-flags", label: "Config & Feature Flags" },
  { id: "compliance-ndpr", label: "Compliance & NDPA" }
] as const;

export function AdminShell() {
  const { user, isLoading, errorMessage, signIn, signOut } = useAuth();

  const healthQuery = useQuery({
    queryKey: ["health-ready"],
    queryFn: fetchHealthReady,
    retry: false
  });

  const meQuery = useQuery({
    queryKey: ["auth-me", user?.access_token],
    queryFn: () => fetchCurrentIdentity(user!.access_token),
    enabled: Boolean(user?.access_token),
    retry: false
  });

  return (
    <div className="min-h-screen flex bg-cream text-ink-900">
      <aside
        className="w-72 shrink-0 bg-surface-dark text-white flex flex-col justify-between p-6 border-r border-ink-700"
        aria-label="Admin Consoles Navigation"
      >
        <div>
          <div className="flex items-center gap-3 pb-6 border-b border-white/10">
            <span
              className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-forest text-gold font-bold"
              aria-hidden="true"
            >
              FB
            </span>
            <div>
              <p className="font-bold tracking-tight text-white">Farm Basics</p>
              <p className="text-xs text-ink-300">Operations Console Shell</p>
            </div>
          </div>

          <p className="mt-6 mb-2 text-xs font-semibold uppercase tracking-wider text-ink-300">
            13 Canonical Consoles
          </p>
          <ul className="space-y-1 text-sm">
            {CANONICAL_CONSOLES.map((consoleItem, index) => (
              <li
                key={consoleItem.id}
                className={
                  index === 0
                    ? "rounded-lg px-3 py-2 bg-white/10 text-white font-semibold"
                    : "rounded-lg px-3 py-1.5 text-ink-300"
                }
              >
                {consoleItem.label}
              </li>
            ))}
          </ul>
        </div>

        <p className="text-xs text-ink-300 pt-6 border-t border-white/10">
          Sprint 0 Shell · DS2 Tokens
        </p>
      </aside>

      <main className="flex-1 p-8 max-w-5xl">
        <header className="flex flex-wrap items-center justify-between gap-4 pb-6 border-b border-ink-150">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">
              Administration &amp; Governance Shell
            </h1>
            <p className="text-sm text-ink-700 mt-1">
              Keycloak OIDC login with server-side fail-closed permission verification (`Rule 8`).
            </p>
          </div>

          <div className="flex items-center gap-3">
            {user ? (
              <button
                type="button"
                onClick={() => void signOut()}
                className="min-h-12 px-5 rounded-full border-2 border-ink-150 bg-white text-ink-900 font-bold hover:border-forest"
              >
                Sign out
              </button>
            ) : (
              <button
                type="button"
                onClick={() => void signIn()}
                className="min-h-12 px-6 rounded-full bg-forest text-white font-bold hover:bg-sage"
              >
                Sign in with Keycloak
              </button>
            )}
          </div>
        </header>

        <section className="grid grid-cols-1 md:grid-cols-2 gap-6 mt-6">
          <article className="rounded-xl border border-ink-150 bg-white p-6">
            <h2 className="text-lg font-bold">API &amp; PostGIS Readiness</h2>
            {healthQuery.isPending && (
              <p className="mt-3 text-sm text-ink-500">Checking /health/ready...</p>
            )}
            {healthQuery.isError && (
              <p className="mt-3 text-sm font-semibold text-clay">
                Unavailable: {(healthQuery.error as Error).message}
              </p>
            )}
            {healthQuery.data && (
              <dl className="mt-3 space-y-2 text-sm">
                <div className="flex justify-between">
                  <dt className="text-ink-500">Status</dt>
                  <dd className="font-bold text-forest">{healthQuery.data.status}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-ink-500">Database</dt>
                  <dd className="font-mono">{healthQuery.data.database}</dd>
                </div>
                {healthQuery.data.postgis && (
                  <div className="flex justify-between gap-2">
                    <dt className="text-ink-500">PostGIS</dt>
                    <dd className="font-mono text-right">{healthQuery.data.postgis}</dd>
                  </div>
                )}
              </dl>
            )}
          </article>

          <article className="rounded-xl border border-ink-150 bg-white p-6">
            <h2 className="text-lg font-bold">Authenticated Session</h2>
            {isLoading && <p className="mt-3 text-sm text-ink-500">Loading session...</p>}
            {errorMessage && (
              <p className="mt-3 text-sm font-semibold text-clay">
                Auth error: {errorMessage}
              </p>
            )}
            {!isLoading && !user && (
              <p className="mt-3 text-sm text-ink-700">
                Not signed in. Admin routes require Keycloak authentication and MFA, followed by a
                live server-side database check on every request.
              </p>
            )}
            {user && (
              <div className="mt-3 space-y-2 text-sm">
                <p>
                  <span className="text-ink-500">OIDC Subject:</span>{" "}
                  <span className="font-mono">{user.profile.sub}</span>
                </p>
                {meQuery.data && (
                  <p>
                    <span className="text-ink-500">API Identity Check:</span>{" "}
                    <span className="font-mono text-forest">
                      {meQuery.data.data.permissionsSource}
                    </span>
                  </p>
                )}
                {meQuery.isError && (
                  <p className="text-clay font-semibold">
                    API check failed: {(meQuery.error as Error).message}
                  </p>
                )}
              </div>
            )}
          </article>
        </section>
      </main>
    </div>
  );
}
