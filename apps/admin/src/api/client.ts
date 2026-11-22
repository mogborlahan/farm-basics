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

export interface ReviewQueueItemDto {
  id: string;
  categoryCode: string;
  categoryName: string;
  subjectRefType: string;
  subjectRefId: string;
  status: string;
  priority: string;
  assignedReviewerId: string | null;
  dueAt: string;
  verdictCode: string | null;
}

export interface TrustOverrideInput {
  identityId: string;
  overriddenTier: "unverified" | "verified" | "trusted";
  reason: string;
  expiryDate?: string;
  isPermanent: boolean;
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

export async function fetchReviewQueue(accessToken: string): Promise<ReviewQueueItemDto[]> {
  const response = await fetch(`${apiBaseUrl}/v1/admin/review-queue`, {
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${accessToken}`
    }
  });

  if (!response.ok) {
    throw new Error(`Review queue fetch returned HTTP ${response.status}`);
  }

  const parsed = (await response.json()) as { data: ReviewQueueItemDto[] };
  return parsed.data;
}

export async function submitReviewVerdict(
  accessToken: string,
  itemId: string,
  verdictCode: string,
  notes?: string
): Promise<{ reviewQueueItemId: string; status: string; verdictCode: string }> {
  const response = await fetch(`${apiBaseUrl}/v1/admin/review-queue/${itemId}/verdict`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ verdictCode, notes })
  });

  if (!response.ok) {
    throw new Error(`Verdict submission returned HTTP ${response.status}`);
  }

  const parsed = (await response.json()) as {
    data: { reviewQueueItemId: string; status: string; verdictCode: string };
  };
  return parsed.data;
}

export async function createTrustOverride(
  accessToken: string,
  input: TrustOverrideInput
): Promise<{ overrideId: string; overriddenTier: string }> {
  const response = await fetch(`${apiBaseUrl}/v1/admin/identities/${input.identityId}/trust-override`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({
      overriddenTier: input.overriddenTier,
      reason: input.reason,
      expiryDate: input.expiryDate && input.expiryDate.length > 0 ? input.expiryDate : null,
      isPermanent: input.isPermanent
    })
  });

  if (!response.ok) {
    throw new Error(`Trust override returned HTTP ${response.status}`);
  }

  const parsed = (await response.json()) as {
    data: { overrideId: string; overriddenTier: string };
  };
  return parsed.data;
}
