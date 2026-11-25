import { useState } from "react";
import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { App, Button, Input, Popconfirm, Space, Table, Tag, Typography } from "antd";
import { useTranslation } from "react-i18next";
import { api, ApiFailure, type Me, type Page, type User } from "../api/client";
import { formatDateTime, type DisplayPrefs } from "../format";

/** Users of the administrator's tenant: search, keyset paging ("load more"), enable/disable, end sessions. */
export function UsersPage({ me, prefs }: { me: Me; prefs: DisplayPrefs }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const qc = useQueryClient();
  const [search, setSearch] = useState("");
  const canWrite = me.permissions.includes("directory.users.write");
  const canSecurity = me.permissions.includes("directory.users.security");
  const list = useInfiniteQuery({
    queryKey: ["users", search],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      api<Page<User>>(`/admin/v1/users?top=50&count=true&search=${encodeURIComponent(search)}${pageParam ? `&cursor=${encodeURIComponent(pageParam)}` : ""}`),
    getNextPageParam: (last) => last.nextCursor,
  });
  const onError = (e: unknown) => void message.error(e instanceof ApiFailure ? e.message : t("common.load_failed"));
  const toggle = useMutation({
    mutationFn: (u: User) => api<null>(`/admin/v1/users/${u.id}/${u.status === "ACTIVE" ? "disable" : "enable"}`, { method: "POST" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
    onError,
  });
  const revoke = useMutation({
    mutationFn: (u: User) => api<number>(`/admin/v1/users/${u.id}/revoke-sessions`, { method: "POST" }),
    onSuccess: (n) => void message.success(t("users.sessions_revoked", { count: n })),
    onError,
  });
  const rows = list.data?.pages.flatMap((p) => p.items) ?? [];
  const total = list.data?.pages[0]?.count ?? null;
  return (
    <Space direction="vertical" size="large" style={{ width: "100%" }}>
      <Typography.Title level={2}>{t("users.title")}</Typography.Title>
      <Input.Search
        allowClear
        aria-label={t("users.search")}
        placeholder={t("users.search")}
        onSearch={setSearch}
        style={{ maxWidth: 360 }}
      />
      {total !== null && <Typography.Text type="secondary">{t("users.count", { count: total })}</Typography.Text>}
      <Table<User>
        rowKey="id"
        loading={list.isPending}
        dataSource={rows}
        pagination={false}
        scroll={{ x: true }}
        columns={[
          { title: t("user.display_name"), dataIndex: "displayName" },
          { title: t("user.upn"), dataIndex: "userPrincipalName", render: (v: string) => <bdi>{v}</bdi> },
          { title: t("user.department"), dataIndex: "department", render: (v: string | null) => v ?? "—" },
          {
            title: t("user.status"),
            dataIndex: "status",
            render: (v: User["status"]) => <Tag color={v === "ACTIVE" ? "green" : "default"}>{t(`user.status_${v.toLowerCase()}`)}</Tag>,
          },
          { title: t("user.last_sign_in"), dataIndex: "lastSignInAt", render: (v: string | null) => formatDateTime(v, prefs) },
          {
            title: t("common.actions"),
            render: (_, u) => (
              <Space wrap>
                {canWrite && u.id !== me.user.id && (
                  <Button size="small" onClick={() => toggle.mutate(u)}>
                    {u.status === "ACTIVE" ? t("users.disable") : t("users.enable")}
                  </Button>
                )}
                {canSecurity && (
                  <Popconfirm title={t("users.revoke_confirm")} onConfirm={() => revoke.mutate(u)} okText={t("common.yes")} cancelText={t("common.no")}>
                    <Button size="small">{t("users.revoke_sessions")}</Button>
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]}
      />
      {list.hasNextPage && (
        <Button onClick={() => void list.fetchNextPage()} loading={list.isFetchingNextPage}>
          {t("common.load_more")}
        </Button>
      )}
    </Space>
  );
}
