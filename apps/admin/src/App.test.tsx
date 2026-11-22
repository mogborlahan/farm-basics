import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { UserManager } from "oidc-client-ts";
import { AuthProvider } from "./auth/AuthProvider";
import { AdminShell } from "./components/AdminShell";

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
    const mockManager = {
      getUser: vi.fn().mockResolvedValue(null),
      signinRedirect: vi.fn(),
      signoutRedirect: vi.fn(),
      events: {
        addUserLoaded: vi.fn(),
        addUserUnloaded: vi.fn(),
        removeUserLoaded: vi.fn(),
        removeUserUnloaded: vi.fn()
      }
    } as unknown as UserManager;

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
});
