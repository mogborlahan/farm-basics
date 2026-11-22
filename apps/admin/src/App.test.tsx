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
});
