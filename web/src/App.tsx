import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { App as AntApp, ConfigProvider, Layout, Menu, Result, Spin, Typography, theme } from "antd";
import enUS from "antd/locale/en_US";
import viVN from "antd/locale/vi_VN";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Link, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { api, type Me } from "./api/client";
import i18n, { isRtl, pickLanguage } from "./i18n";
import { AccountPage } from "./pages/AccountPage";
import { UsersPage } from "./pages/UsersPage";
import { TenantsPage } from "./pages/TenantsPage";
import type { DisplayPrefs } from "./format";

const { Header, Sider, Content } = Layout;

/** Per-response nonce BlueID.Web puts in index.html; Ant Design adds it to the style tags it injects (CSP). */
const cspNonce = document.querySelector<HTMLMetaElement>('meta[name="csp-nonce"]')?.content;

function Shell({ me }: { me: Me }) {
  const { t } = useTranslation();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(false);
  // Header colours come from the theme tokens: the default dark header gave dark text on dark (BUG-007).
  const { token } = theme.useToken();
  const prefs: DisplayPrefs = {
    locale: i18n.language,
    timeZone: me.user.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone,
    hourCycle: me.user.hourCycle,
  };
  const canUsers = me.permissions.includes("directory.users.read");
  const canTenants = me.isPlatform && me.permissions.includes("platform.tenants.read");
  const items = [
    { key: "/", label: <Link to="/">{t("nav.account")}</Link> },
    ...(canUsers ? [{ key: "/admin/users", label: <Link to="/admin/users">{t("nav.users")}</Link> }] : []),
    ...(canTenants ? [{ key: "/platform/tenants", label: <Link to="/platform/tenants">{t("nav.tenants")}</Link> }] : []),
  ];
  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Sider breakpoint="md" collapsedWidth={0} collapsible collapsed={collapsed} onCollapse={setCollapsed} theme="light" width={220}>
        <Typography.Title level={4} style={{ margin: 16 }}>
          BlueID
        </Typography.Title>
        <nav aria-label={t("nav.main")}>
          <Menu mode="inline" selectedKeys={[location.pathname]} items={items} />
        </nav>
      </Sider>
      <Layout>
        <Header
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 16,
            paddingInline: 16,
            background: token.colorBgContainer,
            borderBlockEnd: `1px solid ${token.colorBorderSecondary}`,
          }}
        >
          <Typography.Text strong ellipsis>
            {me.tenantName}
          </Typography.Text>
          <a href="/logout">{t("nav.sign_out")}</a>
        </Header>
        <Content style={{ padding: 16 }}>
          <main>
            <Routes>
              <Route path="/" element={<AccountPage me={me} prefs={prefs} />} />
              {canUsers && <Route path="/admin/users" element={<UsersPage me={me} prefs={prefs} />} />}
              {canTenants && <Route path="/platform/tenants" element={<TenantsPage />} />}
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </main>
        </Content>
      </Layout>
    </Layout>
  );
}

export function App() {
  const { t } = useTranslation();
  const me = useQuery({ queryKey: ["me"], queryFn: () => api<Me>("/me/v1") });
  const lang = me.data ? pickLanguage(me.data.user.locale, me.data.tenantDefaultLocale) : i18n.language;
  useEffect(() => {
    void i18n.changeLanguage(lang);
    document.documentElement.lang = lang;
    document.documentElement.dir = isRtl(lang) ? "rtl" : "ltr";
  }, [lang]);
  const dark = useMemo(() => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false, []);
  return (
    <ConfigProvider
      locale={lang === "vi" ? viVN : enUS}
      direction={isRtl(lang) ? "rtl" : "ltr"}
      csp={cspNonce ? { nonce: cspNonce } : undefined}
      theme={{ algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm, token: { colorPrimary: "#0b57d0", borderRadius: 6 } }}
    >
      <AntApp>
        {me.isPending ? (
          <Spin fullscreen tip={t("common.loading")} />
        ) : me.isError ? (
          <Result status="error" title={t("common.load_failed")} />
        ) : (
          <BrowserRouter basename="/portal">
            <Shell me={me.data} />
          </BrowserRouter>
        )}
      </AntApp>
    </ConfigProvider>
  );
}
