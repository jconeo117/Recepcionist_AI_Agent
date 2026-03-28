import { useState, useEffect, useCallback } from "react";
import { useTheme, planColor, statusColor, businessIcon, fmt } from "../styles/tokens";
import { LoadingState, ErrorBanner } from "../components/Feedback";
import TenantModal from "../components/TenantModal";
import CreateTenantModal from "../components/CreateTenantModal";
import EditTenantModal from "../components/EditTenantModal";
import { fetchTenants, fetchTenantById, suspendTenant, reactivateTenant, createTenant, updateTenant } from "../api/client";

export default function TenantsPage({ onToast }) {
  const { colors: C, styles: s } = useTheme();
  const [tenants, setTenants] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [selected, setSelected] = useState(null);
  const [showCreate, setShowCreate] = useState(false);
  const [showEdit, setShowEdit] = useState(null);
  const [search, setSearch] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchTenants();
      const enriched = await Promise.all(
        (data.tenants || []).map(async (t) => {
          try {
            const detail = await fetchTenantById(t.tenantId);
            return detail;
          } catch {
            return { tenant: t, billing: { planType: "Unknown", billingStatus: "Unknown" } };
          }
        })
      );
      setTenants(enriched);
    } catch (err) {
      setError(err.response?.data?.error || err.message || "Error al cargar tenants");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const filtered = tenants.filter(t =>
    t.tenant.businessName.toLowerCase().includes(search.toLowerCase()) ||
    t.tenant.tenantId.toLowerCase().includes(search.toLowerCase())
  );

  const handleSuspend = async (tenantId) => {
    try {
      await suspendTenant(tenantId);
      onToast?.("Tenant suspendido correctamente", "success");
      await load();
    } catch (err) {
      onToast?.(err.response?.data?.error || "Error al suspender tenant", "error");
      throw err;
    }
  };

  const handleReactivate = async (tenantId) => {
    try {
      await reactivateTenant(tenantId);
      onToast?.("Tenant reactivado correctamente", "success");
      await load();
    } catch (err) {
      onToast?.(err.response?.data?.error || "Error al reactivar tenant", "error");
      throw err;
    }
  };

  const handleCreate = async (payload) => {
    await createTenant(payload);
    onToast?.("Tenant creado correctamente", "success");
    await load();
  };

  const handleUpdate = async (tenantId, payload) => {
    await updateTenant(tenantId, payload);
    onToast?.("Tenant actualizado correctamente", "success");
    await load();
  };

  if (loading) return <LoadingState label="Cargando tenants..." />;
  if (error) return <ErrorBanner message={error} onRetry={load} />;

  return (
    <div className="fade-in">
      <div style={s.header}>
        <div>
          <div style={s.title}>Tenants</div>
          <div style={s.subtitle}>{tenants.length} negocios registrados</div>
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          <button style={s.btn()} onClick={load}>↻ Refrescar</button>
          <button style={s.btn("accent")} onClick={() => setShowCreate(true)}>+ Nuevo Tenant</button>
        </div>
      </div>

      <div style={{ marginBottom: 20 }}>
        <input style={{ ...s.input, width: 280 }} placeholder="Buscar tenant..." value={search}
          onChange={e => setSearch(e.target.value)} />
      </div>

      <div style={s.card}>
        <table style={s.table}>
          <thead>
            <tr>
              {["Negocio", "Tipo", "Providers", "Plan", "Estado", "Activo hasta", ""].map(h =>
                <th key={h} style={s.th}>{h}</th>
              )}
            </tr>
          </thead>
          <tbody>
            {filtered.map(({ tenant, billing }) => (
              <tr key={tenant.tenantId} style={{ cursor: "pointer" }} onClick={() => setSelected({ tenant, billing })}>
                <td style={s.td}>
                  <div style={{ fontWeight: 500 }}>
                    <span style={{ marginRight: 6 }}>{businessIcon(tenant.businessType)}</span>
                    {tenant.businessName}
                  </div>
                  <div style={{ fontSize: 10, color: C.textDim, fontFamily: "monospace", marginTop: 2 }}>{tenant.tenantId}</div>
                </td>
                <td style={{ ...s.td, fontSize: 11, color: C.textMuted }}>{tenant.businessType}</td>
                <td style={{ ...s.td, fontSize: 12 }}>{(tenant.providers || []).length}</td>
                <td style={s.td}><span style={s.chip(planColor(billing?.planType || "None", C))}>{billing?.planType || "Sin Plan"}</span></td>
                <td style={s.td}>
                  <div style={s.row}>
                    <div style={{ ...s.pill, background: statusColor(billing?.billingStatus || "Inactive", C) }} />
                    <span style={{ fontSize: 11, color: statusColor(billing?.billingStatus || "Inactive", C) }}>{billing?.billingStatus || "Inactivo"}</span>
                  </div>
                </td>
                <td style={{ ...s.td, fontSize: 11, color: C.textMuted }}>
                  {billing?.activeUntil ? fmt(billing.activeUntil) : "—"}
                </td>
                <td style={s.td}><span style={{ fontSize: 10, color: C.textDim }}>→</span></td>
              </tr>
            ))}
          </tbody>
        </table>
        {filtered.length === 0 && (
          <div style={{ color: C.textDim, fontSize: 12, padding: "20px 0", textAlign: "center" }}>
            {search ? "No se encontraron resultados." : "No hay tenants registrados."}
          </div>
        )}
      </div>

      {selected && (
        <TenantModal data={selected} onClose={() => setSelected(null)}
          onSuspend={handleSuspend} onReactivate={handleReactivate} 
          onEdit={(t) => { setSelected(null); setShowEdit(t); }} />
      )}

      {showCreate && (
        <CreateTenantModal onClose={() => setShowCreate(false)} onCreate={handleCreate} />
      )}

      {showEdit && (
        <EditTenantModal initialData={showEdit} onClose={() => setShowEdit(null)} onUpdate={handleUpdate} />
      )}
    </div>
  );
}
