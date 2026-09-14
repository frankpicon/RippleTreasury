import keycloak from "../auth/keycloak";
import type {
  ApiProblem,
  Availability,
  EventModel,
  InventorySummary,
  PurchaseRequest,
  PurchaseResponse,
  SalesSummary
} from "./types";

export const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:8080";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly traceId?: string
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  await keycloak.updateToken(30);
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${keycloak.token}`,
      "X-Correlation-ID": crypto.randomUUID(),
      ...init?.headers
    }
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as ApiProblem;
    throw new ApiError(problem.detail ?? problem.title ?? "The request failed.", response.status,
      problem.traceId);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

const purchaseRetryDelaysMilliseconds = [0, 300, 1_000] as const;
const retryableStatuses = new Set([408, 425, 429, 500, 502, 503, 504]);

export function isTransientRequestFailure(error: unknown): boolean {
  return error instanceof TypeError ||
    error instanceof ApiError && retryableStatuses.has(error.status);
}

function wait(milliseconds: number): Promise<void> {
  return new Promise(resolve => window.setTimeout(resolve, milliseconds));
}

async function purchaseWithRetry(
  eventId: string,
  body: PurchaseRequest,
  idempotencyKey: string
): Promise<PurchaseResponse> {
  let lastFailure: unknown;

  for (const delay of purchaseRetryDelaysMilliseconds) {
    if (delay > 0) await wait(delay);

    try {
      return await request<PurchaseResponse>(`/api/v1/events/${eventId}/tickets`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(body)
      });
    } catch (caught) {
      if (!isTransientRequestFailure(caught)) throw caught;
      lastFailure = caught;
    }
  }

  throw lastFailure instanceof Error
    ? lastFailure
    : new Error("The purchase outcome could not be confirmed.");
}

export const api = {
  getEvents: () => request<EventModel[]>("/api/v1/events"),
  getInventorySummaries: () =>
    request<InventorySummary[]>("/api/v1/events/availability"),
  getAvailability: (eventId: string) =>
    request<Availability>(`/api/v1/events/${eventId}/availability`),
  getSales: (eventId: string) =>
    request<SalesSummary>(`/api/v1/reports/events/${eventId}/sales`),
  createEvent: (body: object) =>
    request<EventModel>("/api/v1/events", { method: "POST", body: JSON.stringify(body) }),
  purchase: purchaseWithRetry
};
