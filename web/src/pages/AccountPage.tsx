import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App, Button, Card, Descriptions, Form, Select, Space, Table, Tag, Typography } from "antd";
import { useTranslation } from "react-i18next";
import { api, ApiFailure, type Me, type SessionInfo, type User } from "../api/client";
import { formatDateTime, timeZones, type DisplayPrefs } from "../format";

interface SettingsForm {
  locale: string | null;
  timeZone: string | null;
  hourCycle: "h12" | "h23" | null;
}

/** "My account": profile, display settings shared by every Blue product, and the person's own sessions. */
export function AccountPage({ me, prefs }: { me: Me; prefs: DisplayPrefs }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const qc = useQueryClient();
  const sessions = useQuery({ queryKey: ["me", "sessions"], queryFn: () => api<SessionInfo[]>("/me/v1/sessions") });
  const save = useMutation({
    mutationFn: (v: SettingsForm) => api<User>("/me/v1/settings", { method: "PATCH", body: { locale: v.locale ?? "", timeZone: v.timeZone ?? "", hourCycle: v.hourCycle ?? "" } }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["me"] });
      void message.success(t("account.saved"));
    },
    onError: (e) => void message.error(e instanceof ApiFailure ? e.message : t("common.load_failed")),
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api<number>(`/me/v1/sessions/${id}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["me", "sessions"] }),
  });
  const u = me.user;
  return (
    <Space direction="vertical" size="large" style={{ width: "100%" }}>
      <Typography.Title level={2}>{t("account.title")}</Typography.Title>
      <Card title={t("account.profile")}>
        <Descriptions column={{ xs: 1, md: 2 }}>
          <Descriptions.Item label={t("user.display_name")}>{u.displayName}</Descriptions.Item>
          <Descriptions.Item label={t("user.upn")}>
            <bdi>{u.userPrincipalName}</bdi>
          </Descriptions.Item>
          <Descriptions.Item label={t("user.job_title")}>{u.jobTitle ?? "—"}</Descriptions.Item>
          <Descriptions.Item label={t("user.department")}>{u.department ?? "—"}</Descriptions.Item>
          <Descriptions.Item label={t("user.last_sign_in")}>{formatDateTime(u.lastSignInAt, prefs)}</Descriptions.Item>
        </Descriptions>
      </Card>
      <Card title={t("account.settings")}>
        <Form<SettingsForm>
          layout="vertical"
          initialValues={{ locale: u.locale, timeZone: u.timeZone, hourCycle: u.hourCycle }}
          onFinish={(v) => save.mutate(v)}
          style={{ maxWidth: 480 }}
        >
          <Form.Item name="locale" label={t("account.language")}>
            <Select
              allowClear
              placeholder={t("account.default_value")}
              options={[
                { value: "en", label: "English" },
                { value: "vi", label: "Tiếng Việt" },
              ]}
            />
          </Form.Item>
          <Form.Item name="timeZone" label={t("account.time_zone")}>
            <Select allowClear showSearch placeholder={t("account.default_value")} options={timeZones().map((z) => ({ value: z, label: z }))} />
          </Form.Item>
          <Form.Item name="hourCycle" label={t("account.hour_cycle")}>
            <Select
              allowClear
              placeholder={t("account.default_value")}
              options={[
                { value: "h23", label: t("account.h23") },
                { value: "h12", label: t("account.h12") },
              ]}
            />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={save.isPending}>
            {t("common.save")}
          </Button>
        </Form>
      </Card>
      <Card title={t("account.sessions")}>
        <Table<SessionInfo>
          rowKey="id"
          loading={sessions.isPending}
          dataSource={sessions.data ?? []}
          pagination={false}
          scroll={{ x: true }}
          columns={[
            { title: t("session.device"), dataIndex: "userAgent", render: (v: string | null, s) => <>{v ?? "—"} {s.current && <Tag color="blue">{t("session.this_device")}</Tag>}</> },
            { title: t("session.ip"), dataIndex: "ip", render: (v: string | null) => v ?? "—" },
            { title: t("session.last_seen"), dataIndex: "lastSeenAt", render: (v: string) => formatDateTime(v, prefs) },
            {
              title: t("common.actions"),
              render: (_, s) =>
                s.current ? null : (
                  <Button danger size="small" onClick={() => revoke.mutate(s.id)} loading={revoke.isPending && revoke.variables === s.id}>
                    {t("session.sign_out")}
                  </Button>
                ),
            },
          ]}
        />
      </Card>
    </Space>
  );
}
