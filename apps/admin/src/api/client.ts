export interface HealthReadyResponse {
  status: string;
  database: string;
  postgis?: string;
  timestampUtc: string;
}

export interface AuthMeResponse {
  data: {
    subject: string;
    issuer: string | null;
    permissionsSource: string;
  };
  meta: {
    requestId: string;
  };
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export async function fetchHealthReady(): Promise<HealthReadyResponse> {
  const response = await fetch(`${apiBaseUrl}/health/ready`, {
    headers: { Accept: "application/json" }
  });

  if (!response.ok) {
    throw new Error(`API readiness check returned HTTP ${response.status}`);
  }

  return (await response.json()) as HealthReadyResponse;
}

export async function fetchCurrentIdentity(accessToken: string): Promise<AuthMeResponse> {
  const response = await fetch(`${apiBaseUrl}/v1/auth/me`, {
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${accessToken}`
    }
  });

  if (!response.ok) {
    throw new Error(`Identity endpoint returned HTTP ${response.status}`);
  }

  return (await response.json()) as AuthMeResponse;
}
