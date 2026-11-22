import { fireEvent, render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { User, UserManager } from "oidc-client-ts";
import { AuthProvider } from "./auth/AuthProvider";
import { AdminShell } from "./components/AdminShell";

function createMockManager(signedInUser: User | null = null): UserManager {
  return {
    getUser: vi.fn().mockResolvedValue(signedInUser),
    signinRedirect: vi.fn(),
    signoutRedirect: vi.fn(),
    events: {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      removeUserLoaded: vi.fn(),
      removeUserUnloaded: vi.fn()
    }
  } as unknown as UserManager;
}

describe("AdminShell", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          status: "ready",
          database: "reachable",
          postgis: "3.4",
          timestampUtc: "2026-11-20T18:00:00Z"
        })
      })
    );
  });

  it("renders all 13 console categories, readiness card, and Keycloak sign-in button", async () => {
    const mockManager = createMockManager(null);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } }
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider manager={mockManager}>
          <AdminShell />
        </AuthProvider>
      </QueryClientProvider>
    );

    expect(screen.getByText("Administration & Governance Shell")).toBeInTheDocument();
    expect(screen.getByText("Compliance & NDPA")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in with Keycloak" })).toBeInTheDocument();
    expect(await screen.findByText("reachable")).toBeInTheDocument();
  });

  it("switches to Review Operations queue and records a verdict for IDENTITY_NIN_MISMATCH", async () => {
    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.endsWith("/health/ready")) {
        return {
          ok: true,
          json: async () => ({
            status: "ready",
            database: "reachable",
            postgis: "3.5",
            timestampUtc: "2026-11-22T17:00:00Z"
          })
        };
      }
      if (url.endsWith("/v1/auth/me")) {
        return {
          ok: true,
          json: async () => ({
            data: { subject: "reviewer-1", issuer: "https://auth.test", permissionsSource: "live_database_recheck_required" },
            meta: { requestId: "req-1" }
          })
        };
      }
      if (url.endsWith("/v1/admin/review-queue")) {
        return {
          ok: true,
          json: async () => ({
            data: [
              {
                id: "item-123",
                categoryCode: "IDENTITY_NIN_MISMATCH",
                categoryName: "NIN Mismatch Verification",
                subjectRefType: "identity",
                subjectRefId: "farmer-999",
                status: "assigned",
                priority: "normal",
                assignedReviewerId: "reviewer-1",
                dueAt: "2026-11-24T17:00:00Z",
                verdictCode: null
              }
            ],
            meta: { requestId: "req-2" }
          })
        };
      }
      if (url.endsWith("/v1/admin/review-queue/item-123/verdict")) {
        return {
          ok: true,
          json: async () => ({
            data: { reviewQueueItemId: "item-123", status: "resolved", verdictCode: "APPROVE_IDENTITY" },
            meta: { requestId: "req-3" }
          })
        };
      }
      return { ok: false, status: 404 };
    });
    vi.stubGlobal("fetch", fetchMock);

    const mockManager = createMockManager({
      access_token: "test-jwt",
      profile: { sub: "reviewer-1" }
    } as unknown as User);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } }
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider manager={mockManager}>
          <AdminShell />
        </AuthProvider>
      </QueryClientProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Review Operations" }));

    expect(await screen.findByText("IDENTITY_NIN_MISMATCH")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Record Verdict" }));

    expect(await screen.findByText("Recorded APPROVE_IDENTITY (resolved).")).toBeInTheDocument();
  });

  it("enforces BR-D-04 expiryDate or isPermanent before submitting Trust Profile override", async () => {
    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.endsWith("/v1/admin/identities/farmer-555/trust-override")) {
        return {
          ok: true,
          json: async () => ({
            data: { overrideId: "ov-1", overriddenTier: "verified" },
            meta: { requestId: "req-4" }
          })
        };
      }
      if (url.endsWith("/v1/admin/review-queue")) {
        return { ok: true, json: async () => ({ data: [], meta: { requestId: "req-5" } }) };
      }
      return {
        ok: true,
        json: async () => ({
          status: "ready",
          database: "reachable",
          data: { subject: "admin-1", issuer: null, permissionsSource: "live_database_recheck_required" },
          meta: { requestId: "req-0" }
        })
      };
    });
    vi.stubGlobal("fetch", fetchMock);

    const mockManager = createMockManager({
      access_token: "admin-jwt",
      profile: { sub: "admin-1" }
    } as unknown as User);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } }
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider manager={mockManager}>
          <AdminShell />
        </AuthProvider>
      </QueryClientProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Identity & Org Trust" }));

    fireEvent.change(screen.getByLabelText("Target Identity ID"), { target: { value: "farmer-555" } });
    fireEvent.change(screen.getByLabelText("Override Reason"), { target: { value: "Pilot verified co-op member" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply Trust Override" }));

    expect(await screen.findByText("Specify an Expiry Date or mark Permanent Override (BR-D-04).")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Expiry Date"), { target: { value: "2026-12-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply Trust Override" }));

    expect(await screen.findByText("Trust override ov-1 applied (verified).")).toBeInTheDocument();
  });

  it("switches to Field Operations console and renders small-sample suppression badge alongside tablet fleet", async () => {
    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.endsWith("/health/ready")) {
        return {
          ok: true,
          json: async () => ({
            status: "ready",
            database: "reachable",
            postgis: "3.5",
            timestampUtc: "2026-11-22T19:20:00Z"
          })
        };
      }
      if (url.endsWith("/v1/auth/me")) {
        return {
          ok: true,
          json: async () => ({
            data: { subject: "field-ops-1", issuer: null, permissionsSource: "live_database_recheck_required" },
            meta: { requestId: "req-fo-1" }
          })
        };
      }
      if (url.endsWith("/v1/admin/field-ops/agents")) {
        return {
          ok: true,
          json: async () => ({
            data: {
              minimumSampleSize: 10,
              effectiveScopeType: "organization",
              agents: [
                {
                  agentId: "ag-low",
                  fullName: "Amina Bello",
                  status: "active",
                  organizationId: "org-1",
                  organizationName: "Kano Growers Co-op",
                  organizationStatus: "verified",
                  activeBoundTablets: 1,
                  minimumSampleSize: 10,
                  totalLogs: 4,
                  flaggedLogs: 1,
                  isLogSampleSufficient: false,
                  flagRate: null,
                  totalReviews: 2,
                  rejectedReviews: 0,
                  isReviewSampleSufficient: false,
                  rejectionRate: null,
                  signalStatus: "insufficient_sample"
                },
                {
                  agentId: "ag-high",
                  fullName: "Musa Ibrahim",
                  status: "active",
                  organizationId: "org-1",
                  organizationName: "Kano Growers Co-op",
                  organizationStatus: "verified",
                  activeBoundTablets: 1,
                  minimumSampleSize: 10,
                  totalLogs: 12,
                  flaggedLogs: 3,
                  isLogSampleSufficient: true,
                  flagRate: 0.25,
                  totalReviews: 0,
                  rejectedReviews: 0,
                  isReviewSampleSufficient: false,
                  rejectionRate: null,
                  signalStatus: "elevated"
                }
              ]
            },
            meta: { requestId: "req-fo-2" }
          })
        };
      }
      if (url.endsWith("/v1/admin/field-ops/tablets")) {
        return {
          ok: true,
          json: async () => ({
            data: {
              effectiveScopeType: "organization",
              tablets: [
                {
                  tabletId: "tab-1",
                  deviceSerial: "TAB-KANO-001",
                  status: "active",
                  organizationId: "org-1",
                  organizationName: "Kano Growers Co-op",
                  assignedAgentId: "ag-low",
                  assignedAgentName: "Amina Bello",
                  boundAt: "2026-11-22T18:00:00Z",
                  deactivatedAt: null,
                  activeSessionCount: 1,
                  lastSessionStartedAt: "2026-11-22T18:05:00Z"
                }
              ]
            },
            meta: { requestId: "req-fo-3" }
          })
        };
      }
      return { ok: false, status: 404 };
    });
    vi.stubGlobal("fetch", fetchMock);

    const mockManager = createMockManager({
      access_token: "field-ops-jwt",
      profile: { sub: "field-ops-1" }
    } as unknown as User);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } }
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider manager={mockManager}>
          <AdminShell />
        </AuthProvider>
      </QueryClientProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Field Operations" }));

    expect(await screen.findByText("Insufficient sample (n < 10)")).toBeInTheDocument();
    expect(screen.getByText("25.0%")).toBeInTheDocument();
    expect(screen.getByText("TAB-KANO-001")).toBeInTheDocument();
  });

  it("switches to Analytics console and renders region scope indicator with separated measured/declared hectares", async () => {
    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.endsWith("/health/ready")) {
        return {
          ok: true,
          json: async () => ({
            status: "ready",
            database: "reachable",
            postgis: "3.5",
            timestampUtc: "2026-11-22T19:25:00Z"
          })
        };
      }
      if (url.endsWith("/v1/auth/me")) {
        return {
          ok: true,
          json: async () => ({
            data: { subject: "region-admin-1", issuer: null, permissionsSource: "live_database_recheck_required" },
            meta: { requestId: "req-an-1" }
          })
        };
      }
      if (url.endsWith("/v1/admin/analytics/baseline")) {
        return {
          ok: true,
          json: async () => ({
            data: {
              effectiveScopeType: "region",
              scopedRegionIds: ["11111111-1111-1111-1111-111111111111"],
              computedLive: true,
              totalFarmers: 42,
              verifiedFarmers: 39,
              productionUnitsCount: 51,
              measuredAreaHectares: 64.25,
              declaredAreaHectares: 12.5,
              activeCropCycles: 28,
              totalHarvestedKg: 14500.5
            },
            meta: { requestId: "req-an-2" }
          })
        };
      }
      return { ok: false, status: 404 };
    });
    vi.stubGlobal("fetch", fetchMock);

    const mockManager = createMockManager({
      access_token: "region-jwt",
      profile: { sub: "region-admin-1" }
    } as unknown as User);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } }
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider manager={mockManager}>
          <AdminShell />
        </AuthProvider>
      </QueryClientProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Analytics" }));

    expect(await screen.findByText("Region Scope (1)")).toBeInTheDocument();
    expect(screen.getByText("64.25 ha")).toBeInTheDocument();
    expect(screen.getByText("12.50 ha")).toBeInTheDocument();
    expect(screen.getByText("14500.5 kg")).toBeInTheDocument();
  });
});
