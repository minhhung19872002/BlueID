// Calls BlueID's own APIs with the session cookie (same origin). State-changing calls carry the CSRF header that
// BlueID.Api requires; 401 sends the browser to the sign-in page and back.

export interface ApiError {
  code: string;
  field: string | null;
  message: string;
}

interface Envelope<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: ApiError[];
}

export class ApiFailure extends Error {
  constructor(
    public readonly status: number,
    public readonly errors: ApiError[],
    message: string,
  ) {
    super(message);
  }

  get code(): string {
    return this.errors[0]?.code ?? "internal_error";
  }
}

export function signInUrl(): string {
  return "/login?ReturnUrl=" + encodeURIComponent(window.location.pathname + window.location.search);
}

export async function api<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const method = init.method ?? "GET";
  const headers: Record<string, string> = { Accept: "application/json" };
  if (method !== "GET") {
    headers["X-BlueID-CSRF"] = "1";
  }
  if (init.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }
  const res = await fetch(path, {
    method,
    headers,
    credentials: "same-origin",
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });
  if (res.status === 401) {
    window.location.assign(signInUrl());
    throw new ApiFailure(401, [], "unauthorized");
  }
  const env = (await res.json().catch(() => null)) as Envelope<T> | null;
  if (!res.ok || !env || !env.success) {
    throw new ApiFailure(res.status, env?.errors ?? [], env?.message ?? res.statusText);
  }
  return env.data as T;
}

export type Status = "ACTIVE" | "DISABLED" | "DELETED";

export interface User {
  id: string;
  tenantId: string;
  userPrincipalName: string;
  displayName: string;
  givenName: string | null;
  familyName: string | null;
  mail: string | null;
  jobTitle: string | null;
  department: string | null;
  office: string | null;
  accountType: "MEMBER" | "GUEST";
  source: string;
  status: Status;
  locale: string | null;
  timeZone: string | null;
  hourCycle: "h12" | "h23" | null;
  createdAt: string;
  updatedAt: string;
  lastSignInAt: string | null;
}

export interface Me {
  user: User;
  tenantName: string;
  tenantDefaultLocale: string;
  isPlatform: boolean;
  permissions: string[];
}

export interface SessionInfo {
  id: string;
  createdAt: string;
  lastSeenAt: string;
  ip: string | null;
  userAgent: string | null;
  amr: string[];
  current: boolean;
}

export interface Page<T> {
  items: T[];
  nextCursor: string | null;
  count: number | null;
}

export interface Tenant {
  id: string;
  name: string;
  slug: string;
  status: string;
  type: "PLATFORM" | "CUSTOMER";
  defaultLocale: string;
  defaultTimeZone: string;
  domains: string[];
  users: number;
}
