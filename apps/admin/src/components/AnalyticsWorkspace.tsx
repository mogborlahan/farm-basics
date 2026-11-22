import { useQuery } from "@tanstack/react-query";
import { fetchAnalyticsBaseline } from "../api/client";

interface AnalyticsWorkspaceProps {
  accessToken: string;
}

export function AnalyticsWorkspace({ accessToken }: AnalyticsWorkspaceProps) {
  const analyticsQuery = useQuery({
    queryKey: ["analytics-baseline", accessToken],
    queryFn: () => fetchAnalyticsBaseline(accessToken),
    enabled: Boolean(accessToken),
    retry: false
  });

  return (
    <div className="space-y-6 mt-6">
      <section className="rounded-xl border border-ink-150 bg-white p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h2 className="text-lg font-bold">Analytics Baseline (`FR-AR-001`)</h2>
            <p className="text-sm text-ink-700 mt-1">
              Computed live from domain tables strictly within the caller&apos;s admin role scope.
              Measured geodesic hectares remain separated from declared footprints.
            </p>
          </div>
          {analyticsQuery.data && (
            <span className="rounded-full bg-cream px-3 py-1 text-xs font-semibold text-forest border border-ink-150">
              {analyticsQuery.data.effectiveScopeType === "region"
                ? `Region Scope (${analyticsQuery.data.scopedRegionIds.length})`
                : "Global Scope"}
            </span>
          )}
        </div>

        {!accessToken && (
          <p className="mt-4 text-sm text-ink-500">
            Sign in with Keycloak to view scope-enforced analytics aggregates.
          </p>
        )}

        {accessToken && analyticsQuery.isPending && (
          <p className="mt-4 text-sm text-ink-500">Computing live regional metrics...</p>
        )}

        {analyticsQuery.isError && (
          <p role="alert" className="mt-4 text-sm font-semibold text-clay">
            {(analyticsQuery.error as Error).message}
          </p>
        )}

        {analyticsQuery.data && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mt-6">
            <article className="rounded-xl border border-ink-150 bg-cream/40 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                Total Farmers (Verified)
              </p>
              <p className="mt-2 text-2xl font-bold text-ink-900">
                {analyticsQuery.data.totalFarmers}{" "}
                <span className="text-sm font-normal text-ink-700">
                  ({analyticsQuery.data.verifiedFarmers} active)
                </span>
              </p>
            </article>

            <article className="rounded-xl border border-ink-150 bg-cream/40 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                Production Units
              </p>
              <p className="mt-2 text-2xl font-bold text-ink-900">
                {analyticsQuery.data.productionUnitsCount}
              </p>
            </article>

            <article className="rounded-xl border border-ink-150 bg-cream/40 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                Active Crop Cycles
              </p>
              <p className="mt-2 text-2xl font-bold text-ink-900">
                {analyticsQuery.data.activeCropCycles}
              </p>
            </article>

            <article className="rounded-xl border border-ink-150 bg-white p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-forest">
                Measured Area (Geodesic ha)
              </p>
              <p className="mt-2 text-2xl font-bold font-mono text-forest">
                {analyticsQuery.data.measuredAreaHectares.toFixed(2)} ha
              </p>
            </article>

            <article className="rounded-xl border border-ink-150 bg-white p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-ink-700">
                Declared Area (Manual / Pin ha)
              </p>
              <p className="mt-2 text-2xl font-bold font-mono text-ink-700">
                {analyticsQuery.data.declaredAreaHectares.toFixed(2)} ha
              </p>
            </article>

            <article className="rounded-xl border border-ink-150 bg-white p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                Total Harvested Output
              </p>
              <p className="mt-2 text-2xl font-bold font-mono text-ink-900">
                {analyticsQuery.data.totalHarvestedKg.toFixed(1)} kg
              </p>
            </article>
          </div>
        )}
      </section>
    </div>
  );
}
