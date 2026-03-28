import { useState, useEffect } from "react";
import { useTheme, businessIcon, planColor, statusColor, fmt } from "../styles/tokens";
import api from "../api/client";
import DatabaseExplorer from "./DatabaseExplorer";

const TABS = [
  { id: "info", label: "Información" },
  { id: "database", label: "Base de Datos" },
];

export default function TenantModal({ data, onClose, onSuspend, onReactivate, onEdit }) {
  const { colors: C, styles: s } = useTheme();
  const { tenant, billing } = data;
  const [loading, setLoading] = useState(false);
  const [activeTab, setActiveTab] = useState("info");
  const [dbHealth, setDbHealth] = useState(null);
  const [healthLoading, setHealthLoading] = useState(false);

  useEffect(() => {
    if (activeTab === "database" && tenant.dbType !== "InMemory") {
      fetchHealth();
    }
  }, [activeTab, tenant.tenantId]);

  const fetchHealth = async () => {
    setHealthLoading(true);
    try {
      const resp = await api.get(`/api/admin/tenants/${tenant.tenantId}/database/health`);
      setDbHealth(resp.data.health);
    } catch (err) {
      console.error("Error fetching DB health:", err);
    } finally {
      setHealthLoading(false);
    }
  };

  const handleReinitialize = async () => {
    if (!window.confirm("¿Estás seguro de re-inicializar la DB? Esto creará las tablas faltantes si no existen.")) return;
    setLoading(true);
    try {
      await api.post(`/api/admin/tenants/${tenant.tenantId}/reinitialize`);
      alert("DB Re-inicializada correctamente.");
      fetchHealth();
    } catch (err) {
      alert("Error: " + (err.response?.data?.error || err.message));
    } finally {
      setLoading(false);
    }
  };

  const handleSuspend = async () => {
    setLoading(true);
    try { await onSuspend(tenant.tenantId); onClose(); }
    catch { setLoading(false); }
  };

  const handleReactivate = async () => {
    setLoading(true);
    try { await onReactivate(tenant.tenantId); onClose(); }
    catch { setLoading(false); }
  };

  return (
    <div style={s.modal} onClick={onClose}>
      <div style={{ ...s.modalBox, width: activeTab === "database" ? 700 : 500 }} onClick={e => e.stopPropagation()} className="fade-in">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 20 }}>
          <div>
            <div style={{ fontSize: 18, fontWeight: 700, letterSpacing: "-0.02em" }}>
              {businessIcon(tenant.businessType)} {tenant.businessName}
            </div>
            <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4, fontFamily: "monospace" }}>{tenant.tenantId}</div>
          </div>
          <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
            <span style={s.chip(planColor(billing?.planType || "None", C))}>{billing?.planType || "Sin Plan"}</span>
            <span style={s.chip(statusColor(billing?.billingStatus || "Inactive", C))}>{billing?.billingStatus || "Inactivo"}</span>
          </div>
        </div>

        <div style={{ display: "flex", gap: 4, marginBottom: 16 }}>
          {TABS.map(tab => (
            <button key={tab.id} style={{
              ...s.btn(), fontSize: 10,
              background: activeTab === tab.id ? C.accentDim : C.surfaceHover,
              color: activeTab === tab.id ? C.accent : C.textMuted,
              border: `1px solid ${activeTab === tab.id ? C.accentBorder : "transparent"}`,
            }} onClick={() => setActiveTab(tab.id)}>
              {tab.id === "database" ? "🗄 " : "ℹ "}{tab.label}
            </button>
          ))}
        </div>

        <div style={s.divider} />

        {activeTab === "info" && (
          <div className="fade-in">
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, marginBottom: 16 }}>
              {[
                { label: "Dirección", value: tenant.address || "—" },
                { label: "Teléfono", value: tenant.phone || "—" },
                { label: "Horarios", value: tenant.workingHours },
                { label: "Zona horaria", value: tenant.timeZoneId },
                { label: "Activo hasta", value: billing?.activeUntil ? fmt(billing.activeUntil) : "Sin límite" },
                { label: "Creado", value: fmt(tenant.createdAt) },
                { label: "Tipo de DB", value: tenant.dbType || "InMemory" },
                { label: "Connection String", value: tenant.connectionString ? "••••••••" : "—" },
              ].map(f => (
                <div key={f.label}>
                  <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 3 }}>{f.label}</div>
                  <div style={{ fontSize: 12 }}>{f.value}</div>
                </div>
              ))}
            </div>
            <div style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 8 }}>Servicios</div>
              <div>{(tenant.services || []).map(sv => <span key={sv} style={s.tag}>{sv}</span>)}</div>
            </div>
            <div style={{ marginBottom: 20 }}>
              <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 10 }}>
                Providers ({(tenant.providers || []).length})
              </div>
              {(tenant.providers || []).map(p => (
                <div key={p.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "8px 0", borderBottom: `1px solid ${C.border}` }}>
                  <div>
                    <div style={{ fontSize: 12, fontWeight: 500 }}>{p.name}</div>
                    <div style={{ fontSize: 10, color: C.textMuted }}>{p.role}</div>
                  </div>
                  <div style={{ textAlign: "right" }}>
                    <div style={{ fontSize: 10, color: C.textDim }}>{p.startTime} – {p.endTime}</div>
                    <div style={{ fontSize: 10, color: C.textDim }}>{p.slotDurationMinutes} min/slot</div>
                  </div>
                </div>
              ))}
            </div>
            <div style={{ background: C.surfaceHover, border: `1px solid ${C.border}`, borderRadius: 6, padding: "8px 12px", marginBottom: 20 }}>
              <div style={{ fontSize: 10, color: C.textDim, marginBottom: 3 }}>WEBHOOK</div>
              <div style={{ fontSize: 11, color: C.textMuted, fontFamily: "monospace" }}>/api/twilio/{tenant.tenantId}</div>
            </div>
            {tenant.dbType !== "InMemory" && (
              <div style={{ fontSize: 11, color: C.orange, background: C.orangeDim, padding: "8px 12px", borderRadius: 6, border: `1px solid ${C.orangeBorder}` }}>
                ⓘ Este tenant usa {tenant.dbType}. Los proveedores se gestionan directamente en su base de datos.
              </div>
            )}
          </div>
        )}

        {activeTab === "database" && (
          <div className="fade-in" style={{ marginBottom: 20 }}>
            {tenant.dbType !== "InMemory" && dbHealth && (
              <div style={{ background: C.surfaceHover, border: `1px solid ${C.border}`, borderRadius: 8, padding: 14, marginBottom: 16 }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
                  <div style={{ fontSize: 11, fontWeight: 700, color: C.textDim, textTransform: "uppercase" }}>Estado de Tablas (Cliente)</div>
                  <button style={{ ...s.btn(), fontSize: 9, padding: "4px 8px" }} onClick={handleReinitialize} disabled={loading}>
                     {loading ? "..." : "⚡ Re-inicializar DB"}
                  </button>
                </div>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8 }}>
                  {Object.entries(dbHealth).map(([table, stat]) => (
                    <div key={table} style={{ display: "flex", justifyContent: "space-between", padding: "6px 10px", background: C.bg, borderRadius: 6, border: `1px solid ${stat.Exists ? C.border : C.redBorder}` }}>
                      <span style={{ fontSize: 11, color: stat.Exists ? C.text : C.red }}>{table}</span>
                      <span style={{ fontSize: 10, color: C.textMuted }}>{stat.Exists ? `${stat.RowCount} filas` : "MISSING"}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
            <DatabaseExplorer tenantId={tenant.tenantId} dbType={tenant.dbType} />
          </div>
        )}

        <div style={s.divider} />
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button style={s.btn()} onClick={() => onEdit(tenant)}>Editar</button>
          {billing?.billingStatus === "Active" ? (
            <button style={s.btn("danger")} onClick={handleSuspend} disabled={loading}>
              {loading ? "..." : "Suspender"}
            </button>
          ) : (
            <button style={s.btn("accent")} onClick={handleReactivate} disabled={loading}>
              {loading ? "..." : "Reactivar"}
            </button>
          )}
          <button style={s.btn()} onClick={onClose}>Cerrar</button>
        </div>
      </div>
    </div>
  );
}
