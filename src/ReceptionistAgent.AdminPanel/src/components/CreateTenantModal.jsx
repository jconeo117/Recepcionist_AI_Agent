import { useState } from "react";
import { useTheme } from "../styles/tokens";

const BUSINESS_TYPES = ["clinic", "salon", "wellness", "nails", "workshop", "restaurant", "gym", "other"];
const DB_TYPES = ["InMemory", "SqlServer", "PostgreSql"];
const TIMEZONES = ["America/Bogota", "America/New_York", "America/Mexico_City", "America/Lima", "America/Santiago", "UTC"];
const MESSAGE_PROVIDERS = ["Twilio", "Meta"];

const emptyProvider = () => ({
  id: "", name: "", role: "", workingDays: [],
  startTime: "09:00", endTime: "18:00", slotDurationMinutes: 30,
});

const STEPS = ["Básicos", "Base de Datos", "Mensajería", "Servicios"];

export default function CreateTenantModal({ onClose, onCreate }) {
  const { colors: C, styles: s } = useTheme();
  const [step, setStep] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const [form, setForm] = useState({
    tenantId: "", businessName: "", businessType: "clinic",
    username: "", passwordHash: "",
    timeZoneId: "America/Bogota", address: "", phone: "", phoneCountryCode: "", workingHours: "",
    dbType: "InMemory", connectionString: "",
    messageProvider: "Twilio", messageProviderAccount: "", messageProviderToken: "", messageProviderPhone: "",
    services: [""],
  });

  const set = (key, val) => setForm(f => ({ ...f, [key]: val }));
  const addService = () => set("services", [...form.services, ""]);
  const removeService = (i) => set("services", form.services.filter((_, idx) => idx !== i));
  const updateService = (i, val) => { const copy = [...form.services]; copy[i] = val; set("services", copy); };

  const canNext = () => {
    if (step === 0) return form.tenantId.trim() && form.businessName.trim();
    if (step === 1) return form.dbType === "InMemory" || form.connectionString.trim();
    return true;
  };

  const handleSubmit = async () => {
    setLoading(true);
    setError(null);
    try {
      const payload = {
        tenantId: form.tenantId.trim().toLowerCase().replace(/\s+/g, "-"),
        businessName: form.businessName.trim(),
        username: form.username.trim() || undefined,
        passwordHash: form.passwordHash.trim() || undefined,
        businessType: form.businessType,
        timeZoneId: form.timeZoneId,
        address: form.address.trim(),
        phone: form.phone.trim(),
        phoneCountryCode: form.phoneCountryCode.trim(),
        workingHours: form.workingHours.trim(),
        dbType: form.dbType,
        connectionString: form.dbType !== "InMemory" ? form.connectionString.trim() : "",
        messageProvider: form.messageProvider,
        messageProviderAccount: form.messageProviderAccount.trim(),
        messageProviderToken: form.messageProviderToken.trim(),
        messageProviderPhone: form.messageProviderPhone.trim(),
        services: form.services.filter(sv => sv.trim()),
        providers: [],
      };
      await onCreate(payload);
      onClose();
    } catch (err) {
      setError(err.response?.data?.error || err.message || "Error al crear tenant");
      setLoading(false);
    }
  };

  const inputStyle = { ...s.input, marginBottom: 10 };
  const labelStyle = { fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 4, display: "block" };
  const selectStyle = { ...s.input, marginBottom: 10, appearance: "auto" };

  return (
    <div style={s.modal} onClick={onClose}>
      <div style={{ ...s.modalBox, width: 560 }} onClick={e => e.stopPropagation()} className="fade-in">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 20 }}>
          <div>
            <div style={{ fontSize: 18, fontWeight: 700, letterSpacing: "-0.02em" }}>+ Nuevo Tenant</div>
            <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>Paso {step + 1} de {STEPS.length} — {STEPS[step]}</div>
          </div>
          <button style={s.btn()} onClick={onClose}>✕</button>
        </div>

        <div style={{ display: "flex", gap: 4, marginBottom: 20 }}>
          {STEPS.map((label, i) => (
            <div key={label} style={{ flex: 1, height: 3, borderRadius: 2, background: i <= step ? C.accent : C.border, transition: "background 0.2s" }} />
          ))}
        </div>

        {step === 0 && (
          <div className="fade-in">
            <label style={labelStyle}>Tenant ID *</label>
            <input style={inputStyle} placeholder="ej: clinica-salud-total" value={form.tenantId} onChange={e => set("tenantId", e.target.value)} />
            <label style={labelStyle}>Nombre del Negocio *</label>
            <input style={inputStyle} placeholder="ej: Clínica Salud Total" value={form.businessName} onChange={e => set("businessName", e.target.value)} />
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <div>
                <label style={labelStyle}>Usuario (Client Dashboard)</label>
                <input style={inputStyle} placeholder="ej: admin" value={form.username} onChange={e => set("username", e.target.value)} />
              </div>
              <div>
                <label style={labelStyle}>Contraseña</label>
                <input style={inputStyle} type="password" placeholder="••••••••" value={form.passwordHash} onChange={e => set("passwordHash", e.target.value)} />
              </div>
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <div>
                <label style={labelStyle}>Tipo de Negocio</label>
                <select style={selectStyle} value={form.businessType} onChange={e => set("businessType", e.target.value)}>
                  {BUSINESS_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>
              <div>
                <label style={labelStyle}>Zona Horaria</label>
                <select style={selectStyle} value={form.timeZoneId} onChange={e => set("timeZoneId", e.target.value)}>
                  {TIMEZONES.map(tz => <option key={tz} value={tz}>{tz}</option>)}
                </select>
              </div>
            </div>
            <label style={labelStyle}>Dirección</label>
            <input style={inputStyle} placeholder="ej: Calle 30 #25-10, Montería" value={form.address} onChange={e => set("address", e.target.value)} />
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <div style={{ display: "grid", gridTemplateColumns: "80px 1fr", gap: 8 }}>
                <div>
                  <label style={labelStyle}>Prefijo</label>
                  <input style={inputStyle} placeholder="57" value={form.phoneCountryCode} onChange={e => set("phoneCountryCode", e.target.value)} />
                </div>
                <div>
                  <label style={labelStyle}>Teléfono</label>
                  <input style={inputStyle} placeholder="314-111-0001" value={form.phone} onChange={e => set("phone", e.target.value)} />
                </div>
              </div>
              <div>
                <label style={labelStyle}>Horario de Trabajo</label>
                <input style={inputStyle} placeholder="ej: Lun-Vie: 8AM-6PM" value={form.workingHours} onChange={e => set("workingHours", e.target.value)} />
              </div>
            </div>
          </div>
        )}

        {step === 1 && (
          <div className="fade-in">
            <label style={labelStyle}>Tipo de Base de Datos</label>
            <select style={selectStyle} value={form.dbType} onChange={e => set("dbType", e.target.value)}>
              {DB_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
            {form.dbType !== "InMemory" && (
              <>
                <label style={labelStyle}>Connection String *</label>
                <input style={inputStyle} placeholder="Server=... / Host=..." value={form.connectionString} onChange={e => set("connectionString", e.target.value)} />
                <div style={{ fontSize: 10, color: C.textDim, marginTop: -6, marginBottom: 12 }}>
                  ⓘ La base de datos debe tener la tabla <code style={{ color: C.textMuted }}>Bookings</code> creada.
                </div>
              </>
            )}
            {form.dbType === "InMemory" && (
              <div style={{ padding: "14px 16px", borderRadius: 6, background: C.accentDim, border: `1px solid ${C.accentBorder}`, fontSize: 11, color: C.accent }}>
                ⓘ InMemory: las citas se pierden al reiniciar la API. Ideal para pruebas rápidas.
              </div>
            )}
          </div>
        )}

        {step === 2 && (
          <div className="fade-in">
             <label style={labelStyle}>Proveedor de Mensajería</label>
             <select style={selectStyle} value={form.messageProvider} onChange={e => set("messageProvider", e.target.value)}>
               {MESSAGE_PROVIDERS.map(t => <option key={t} value={t}>{t}</option>)}
             </select>
             
             <label style={labelStyle}>Account SID / Phone Number ID</label>
             <input style={inputStyle} placeholder="Dejar en blanco para usar el global / sandbox" value={form.messageProviderAccount} onChange={e => set("messageProviderAccount", e.target.value)} />
             
             <label style={labelStyle}>Auth Token / Access Token</label>
             <input style={inputStyle} placeholder="Dejar en blanco para usar el global / sandbox" value={form.messageProviderToken} onChange={e => set("messageProviderToken", e.target.value)} />

             <label style={labelStyle}>Número de Envío (WhatsApp)</label>
             <input style={inputStyle} placeholder={"ej: whatsapp:+18881234567"} value={form.messageProviderPhone} onChange={e => set("messageProviderPhone", e.target.value)} />
             <div style={{ fontSize: 10, color: C.textDim, marginTop: -6, marginBottom: 12 }}>
                ⓘ Si los campos se dejan vacíos el sistema usará el Sandbox de manera global automáticamente.
             </div>
          </div>
        )}

        {step === 3 && (
          <div className="fade-in">
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
              <label style={{ ...labelStyle, marginBottom: 0 }}>Servicios</label>
              <button style={s.btn()} onClick={addService}>+ Agregar</button>
            </div>
            {form.services.map((sv, i) => (
              <div key={i} style={{ display: "flex", gap: 6, marginBottom: 6 }}>
                <input style={{ ...s.input, flex: 1 }} placeholder={`Servicio ${i + 1}`} value={sv} onChange={e => updateService(i, e.target.value)} />
                {form.services.length > 1 && <button style={{ ...s.btn("danger"), padding: "6px 10px" }} onClick={() => removeService(i)}>✕</button>}
              </div>
            ))}
          </div>
        )}


        {error && (
          <div style={{ padding: "10px 14px", borderRadius: 6, background: C.redDim, border: "1px solid rgba(255,68,68,0.2)", color: C.red, fontSize: 11, marginTop: 12 }}>
            ⚠ {error}
          </div>
        )}

        <div style={{ ...s.divider, margin: "20px 0 16px" }} />
        <div style={{ display: "flex", justifyContent: "space-between" }}>
          <div>{step > 0 && <button style={s.btn()} onClick={() => setStep(step - 1)}>← Anterior</button>}</div>
          <div style={{ display: "flex", gap: 8 }}>
            {step < STEPS.length - 1 ? (
              <button style={s.btn("accent")} disabled={!canNext()} onClick={() => setStep(step + 1)}>Siguiente →</button>
            ) : (
              <button style={s.btn("accent")} disabled={loading || !canNext()} onClick={handleSubmit}>{loading ? "Creando..." : "Crear Tenant"}</button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
