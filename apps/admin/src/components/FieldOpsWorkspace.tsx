import { useQuery } from "@tanstack/react-query";
import { fetchFieldOpsAgents, fetchFieldOpsTablets } from "../api/client";

interface FieldOpsWorkspaceProps {
  accessToken: string;
}

export function FieldOpsWorkspace({ accessToken }: FieldOpsWorkspaceProps) {
  const agentsQuery = useQuery({
    queryKey: ["field-ops-agents", accessToken],
    queryFn: () => fetchFieldOpsAgents(accessToken),
    enabled: Boolean(accessToken),
    retry: false
  });

  const tabletsQuery = useQuery({
    queryKey: ["field-ops-tablets", accessToken],
    queryFn: () => fetchFieldOpsTablets(accessToken),
    enabled: Boolean(accessToken),
    retry: false
  });

  return (
    <div className="space-y-6 mt-6">
      <section className="rounded-xl border border-ink-150 bg-white p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h2 className="text-lg font-bold">Extension Agent Roster &amp; Quality Signals</h2>
            <p className="text-sm text-ink-700 mt-1">
              Scoped by organization/region (`FR-FO-001` &amp; `FR-FO-003`). Flag &amp; rejection ratios
              are suppressed below the minimum sample size to prevent small-sample false signals.
            </p>
          </div>
          {agentsQuery.data && (
            <span className="rounded-full bg-cream px-3 py-1 text-xs font-semibold text-ink-700 border border-ink-150">
              Scope: {agentsQuery.data.effectiveScopeType} · Min n ={" "}
              {agentsQuery.data.minimumSampleSize}
            </span>
          )}
        </div>

        {!accessToken && (
          <p className="mt-4 text-sm text-ink-500">
            Sign in with Keycloak to inspect the agent roster and quality signals.
          </p>
        )}

        {accessToken && agentsQuery.isPending && (
          <p className="mt-4 text-sm text-ink-500">Loading agent roster...</p>
        )}

        {agentsQuery.isError && (
          <p role="alert" className="mt-4 text-sm font-semibold text-clay">
            {(agentsQuery.error as Error).message}
          </p>
        )}

        {agentsQuery.data && agentsQuery.data.agents.length === 0 && (
          <p className="mt-4 text-sm text-ink-500">No extension agents found within scope.</p>
        )}

        {agentsQuery.data && agentsQuery.data.agents.length > 0 && (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b border-ink-150 text-ink-500">
                  <th className="py-2 pr-3">Agent</th>
                  <th className="py-2 pr-3">Organization</th>
                  <th className="py-2 pr-3">Logs (Flagged/Total)</th>
                  <th className="py-2 pr-3">Flag Rate</th>
                  <th className="py-2 pr-3">Rejection Rate</th>
                  <th className="py-2">Signal</th>
                </tr>
              </thead>
              <tbody>
                {agentsQuery.data.agents.map((agent) => (
                  <tr key={`${agent.organizationId}-${agent.agentId}`} className="border-b border-ink-150/60">
                    <td className="py-2.5 pr-3">
                      <div className="font-semibold text-ink-900">{agent.fullName}</div>
                      <div className="font-mono text-xs text-ink-500">
                        {agent.activeBoundTablets} bound tablet(s)
                      </div>
                    </td>
                    <td className="py-2.5 pr-3">{agent.organizationName}</td>
                    <td className="py-2.5 pr-3 font-mono text-xs">
                      {agent.flaggedLogs}/{agent.totalLogs}
                    </td>
                    <td className="py-2.5 pr-3">
                      {agent.isLogSampleSufficient && agent.flagRate !== null ? (
                        <span className="font-mono font-semibold text-ink-900">
                          {(agent.flagRate * 100).toFixed(1)}%
                        </span>
                      ) : (
                        <span className="inline-flex rounded-full bg-cream px-2.5 py-0.5 text-xs font-semibold text-ink-700 border border-ink-150">
                          Insufficient sample (n &lt; {agent.minimumSampleSize})
                        </span>
                      )}
                    </td>
                    <td className="py-2.5 pr-3">
                      {agent.isReviewSampleSufficient && agent.rejectionRate !== null ? (
                        <span className="font-mono font-semibold text-ink-900">
                          {(agent.rejectionRate * 100).toFixed(1)}%
                        </span>
                      ) : (
                        <span className="text-xs text-ink-500">
                          n = {agent.totalReviews} &lt; {agent.minimumSampleSize}
                        </span>
                      )}
                    </td>
                    <td className="py-2.5">
                      <span
                        className={
                          agent.signalStatus === "elevated"
                            ? "inline-flex rounded-full bg-clay/15 px-2.5 py-0.5 text-xs font-bold text-clay"
                            : agent.signalStatus === "insufficient_sample"
                              ? "inline-flex rounded-full bg-cream px-2.5 py-0.5 text-xs font-semibold text-ink-500"
                              : "inline-flex rounded-full bg-forest/15 px-2.5 py-0.5 text-xs font-bold text-forest"
                        }
                      >
                        {agent.signalStatus}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="rounded-xl border border-ink-150 bg-white p-6">
        <h2 className="text-lg font-bold">Tablet Fleet Status (`FR-FO-002`)</h2>
        <p className="text-sm text-ink-700 mt-1">
          Device-level hardware bindings, active field sessions, and deactivation status.
        </p>

        {accessToken && tabletsQuery.isPending && (
          <p className="mt-4 text-sm text-ink-500">Loading tablet fleet...</p>
        )}

        {tabletsQuery.isError && (
          <p role="alert" className="mt-4 text-sm font-semibold text-clay">
            {(tabletsQuery.error as Error).message}
          </p>
        )}

        {tabletsQuery.data && tabletsQuery.data.tablets.length === 0 && (
          <p className="mt-4 text-sm text-ink-500">No bound tablets in current scope.</p>
        )}

        {tabletsQuery.data && tabletsQuery.data.tablets.length > 0 && (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b border-ink-150 text-ink-500">
                  <th className="py-2 pr-3">Device Serial</th>
                  <th className="py-2 pr-3">Organization</th>
                  <th className="py-2 pr-3">Assigned Agent</th>
                  <th className="py-2 pr-3">Status</th>
                  <th className="py-2">Active Sessions</th>
                </tr>
              </thead>
              <tbody>
                {tabletsQuery.data.tablets.map((tablet) => (
                  <tr key={tablet.tabletId} className="border-b border-ink-150/60">
                    <td className="py-2.5 pr-3 font-mono text-xs">{tablet.deviceSerial}</td>
                    <td className="py-2.5 pr-3">{tablet.organizationName ?? "Unbound"}</td>
                    <td className="py-2.5 pr-3">{tablet.assignedAgentName ?? "Unassigned"}</td>
                    <td className="py-2.5 pr-3">
                      <span
                        className={
                          tablet.status === "active"
                            ? "font-semibold text-forest"
                            : "font-semibold text-clay"
                        }
                      >
                        {tablet.status}
                      </span>
                    </td>
                    <td className="py-2.5 font-mono text-xs">{tablet.activeSessionCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
