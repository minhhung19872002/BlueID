import { useQuery } from "@tanstack/react-query";
import { Space, Table, Tag, Typography } from "antd";
import { useTranslation } from "react-i18next";
import { api, type Tenant } from "../api/client";

/** Platform operators: every tenant of the service. */
export function TenantsPage() {
  const { t } = useTranslation();
  const list = useQuery({ queryKey: ["tenants"], queryFn: () => api<Tenant[]>("/admin/v1/platform/tenants") });
  return (
    <Space direction="vertical" size="large" style={{ width: "100%" }}>
      <Typography.Title level={2}>{t("tenants.title")}</Typography.Title>
      <Table<Tenant>
        rowKey="id"
        loading={list.isPending}
        dataSource={list.data ?? []}
        pagination={false}
        scroll={{ x: true }}
        columns={[
          { title: t("tenants.name"), dataIndex: "name" },
          { title: t("tenants.slug"), dataIndex: "slug" },
          { title: t("tenants.type"), dataIndex: "type", render: (v: Tenant["type"]) => <Tag>{t(`tenants.type_${v.toLowerCase()}`)}</Tag> },
          { title: t("tenants.domains"), dataIndex: "domains", render: (v: string[]) => v.join(", ") },
          { title: t("tenants.users"), dataIndex: "users" },
          { title: t("tenants.locale"), dataIndex: "defaultLocale" },
        ]}
      />
    </Space>
  );
}
