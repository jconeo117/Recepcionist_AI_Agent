import React, { useState, useEffect } from 'react';
import axios from 'axios';
import DashboardLayout from '../components/DashboardLayout';
import Topbar from '../components/Topbar';

const DAYS = [
  { value: 'Monday', label: 'Lunes' },
  { value: 'Tuesday', label: 'Martes' },
  { value: 'Wednesday', label: 'Miércoles' },
  { value: 'Thursday', label: 'Jueves' },
  { value: 'Friday', label: 'Viernes' },
  { value: 'Saturday', label: 'Sábado' },
  { value: 'Sunday', label: 'Domingo' }
];

const DEFAULT_FORM = {
  name: '', role: '', workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
  startTime: '09:00:00', endTime: '18:00:00', slotDurationMinutes: 30, isAvailable: true
};

const Providers = () => {
  const [providers, setProviders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingProvider, setEditingProvider] = useState(null);
  const [formData, setFormData] = useState(DEFAULT_FORM);
  const [saving, setSaving] = useState(false);

  const fetchProviders = async () => {
    try {
      const res = await axios.get('/api/dashboard/providers');
      setProviders(res.data);
    } catch (err) {
      console.error('Error fetching providers', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchProviders(); }, []);

  const openCreate = () => {
    setEditingProvider(null);
    setFormData(DEFAULT_FORM);
    setShowModal(true);
  };

  const openEdit = (p) => {
    setEditingProvider(p);
    setFormData({
      id: p.id,
      name: p.name || '',
      role: p.role || '',
      workingDays: p.workingDays || [],
      startTime: p.startTime?.substring(0, 5) || '09:00', // Convert hh:mm:ss to hh:mm
      endTime: p.endTime?.substring(0, 5) || '18:00',
      slotDurationMinutes: p.slotDurationMinutes || 30,
      isAvailable: p.isAvailable !== false
    });
    setShowModal(true);
  };

  const handleDelete = async (e, id) => {
    e.stopPropagation();
    if (!window.confirm("¿Seguro que deseas eliminar este proveedor?")) return;
    try {
      await axios.delete(`/api/dashboard/providers/${id}`);
      fetchProviders();
    } catch (err) {
      console.error(err);
      alert("Error eliminando proveedor");
    }
  };

  const handleSave = async (e) => {
    e.preventDefault();
    setSaving(true);
    
    // Format times for backend span (hh:mm:00)
    const payload = {
      ...formData,
      startTime: formData.startTime.length === 5 ? `${formData.startTime}:00` : formData.startTime,
      endTime: formData.endTime.length === 5 ? `${formData.endTime}:00` : formData.endTime,
    };

    try {
      if (editingProvider) {
        await axios.put(`/api/dashboard/providers/${editingProvider.id}`, payload);
      } else {
        await axios.post('/api/dashboard/providers', payload);
      }
      setShowModal(false);
      fetchProviders();
    } catch (err) {
      console.error(err);
      alert("Error guardando el proveedor");
    } finally {
      setSaving(false);
    }
  };

  const toggleDay = (day) => {
    setFormData(prev => ({
      ...prev,
      workingDays: prev.workingDays.includes(day)
        ? prev.workingDays.filter(d => d !== day)
        : [...prev.workingDays, day]
    }));
  };

  const getInitials = (name) => name?.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2) || '??';
  
  const getRandomGradient = (id) => {
    const gradients = ['from-[#2D5BE3] to-[#7C3AED]', 'from-[#059669] to-[#10B981]', 'from-[#F59E0B] to-[#EF4444]', 'from-[#DC2626] to-[#F87171]'];
    const idx = id ? Array.from(id).reduce((acc, char) => acc + char.charCodeAt(0), 0) % gradients.length : 0;
    return gradients[idx];
  };

  if (loading) return (
    <DashboardLayout>
      <Topbar title="Proveedores" />
      <div className="flex-1 flex items-center justify-center"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--accent)]"></div></div>
    </DashboardLayout>
  );

  return (
    <DashboardLayout>
      <Topbar title="Proveedores" />
      <div className="flex-1 overflow-y-auto p-7">
        <div className="flex justify-between items-center mb-6">
          <div className="text-[14px] text-[var(--text-secondary)]">
            Gestiona los especialistas y sus horarios de atención.
          </div>
          <button onClick={openCreate} className="px-4 py-2 bg-[var(--accent)] text-white text-[13px] font-bold rounded-[var(--radius)] hover:bg-[var(--accent-hover)] transition-colors shadow-sm focus:outline-none focus:ring-2 focus:ring-[var(--accent)] focus:ring-offset-2 focus:ring-offset-[var(--background)]">
            + Nuevo Proveedor
          </button>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {providers.map((p) => (
            <div key={p.id} onClick={() => openEdit(p)} className="relative bg-[var(--surface)] border border-[var(--border)] rounded-[var(--radius)] p-5 text-center transition-all duration-300 cursor-pointer hover:shadow-lg hover:-translate-y-1 group hover:border-[var(--accent)]">
              
              <div className="absolute top-3 right-3 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                <button onClick={(e) => handleDelete(e, p.id)} className="w-8 h-8 flex justify-center items-center bg-[var(--surface-hover)] rounded-md text-red-500 hover:bg-red-500 hover:text-white transition-colors border border-[var(--border)]" title="Eliminar" aria-label="Eliminar">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path><line x1="10" y1="11" x2="10" y2="17"></line><line x1="14" y1="11" x2="14" y2="17"></line></svg>
                </button>
              </div>

              <div className={`w-14 h-14 rounded-2xl mx-auto mb-3 flex items-center justify-center text-white text-[20px] font-bold bg-gradient-to-br shadow-inner ${getRandomGradient(p.id)} ${!p.isAvailable && 'grayscale opacity-60'}`}>
                {getInitials(p.name)}
              </div>
              <div className="text-[15px] font-bold mb-1 tracking-tight text-[var(--text-primary)] flex justify-center items-center gap-2">
                {p.name}
              </div>
              <div className="text-[12px] text-[var(--text-secondary)] mb-4">{p.role || 'Especialista'}</div>

              <div className="flex justify-center items-center mb-4">
                <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold tracking-wide uppercase ${p.isAvailable ? 'bg-green-500/10 text-green-500 border border-green-500/20' : 'bg-red-500/10 text-red-500 border border-red-500/20'}`}>
                  {p.isAvailable ? 'Activo' : 'Inactivo'}
                </span>
              </div>
              
              <div className="flex justify-between items-center pt-4 border-t border-[var(--border)] mt-2">
                <div className="text-left">
                  <div className="text-[10px] font-bold text-[var(--text-muted)] tracking-wider uppercase mb-0.5">Horario</div>
                  <div className="font-display text-[13px] font-semibold text-[var(--text-primary)]">{p.startTime?.substring(0,5)} - {p.endTime?.substring(0,5)}</div>
                </div>
                <div className="text-right">
                  <div className="text-[10px] font-bold text-[var(--text-muted)] tracking-wider uppercase mb-0.5">Días</div>
                  <div className="font-display text-[13px] font-semibold text-[var(--text-primary)]">{p.workingDays?.length || 0} de la semana</div>
                </div>
              </div>
            </div>
          ))}
          {providers.length === 0 && (
            <div className="col-span-full py-20 flex flex-col items-center justify-center text-center border-2 border-dashed border-[var(--border)] rounded-2xl bg-[var(--surface)]/50">
              <div className="w-16 h-16 bg-[var(--surface-hover)] rounded-full flex items-center justify-center mb-4 shadow-sm border border-[var(--border)]">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path></svg>
              </div>
              <h3 className="text-[15px] font-bold text-[var(--text-primary)] mb-2 uppercase tracking-wide">Sin proveedores</h3>
              <p className="text-[13px] text-[var(--text-secondary)] mb-6 max-w-[300px] leading-relaxed">Agrega a tu equipo para permitir que el agente de inteligencia artificial asigne citas en los calendarios individuales.</p>
              <button onClick={openCreate} className="px-5 py-2.5 text-[13px] font-bold text-[var(--accent)] bg-[var(--accent)]/10 hover:bg-[var(--accent)]/20 rounded-lg transition-colors border border-[var(--accent)]/20 hover:border-[var(--accent)]/30">
                + Crear el primero
              </button>
            </div>
          )}
        </div>
      </div>

      {showModal && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4 antialiased overflow-y-auto">
          <div className="bg-[var(--surface)] w-full max-w-lg rounded-2xl shadow-[0_20px_60px_-15px_rgba(0,0,0,0.5)] border border-[var(--border)] transform transition-all scale-100 flex flex-col my-8">
            <div className="p-6 border-b border-[var(--border)]">
              <h2 className="text-xl font-bold font-display text-[var(--text-primary)]">{editingProvider ? 'Editar Proveedor' : 'Nuevo Proveedor'}</h2>
              <p className="text-[13px] text-[var(--text-secondary)] mt-1">Configura la información y disponibilidad médica para el motor de agendamiento.</p>
            </div>
            
            <form onSubmit={handleSave} className="p-6 space-y-5">
              <div>
                <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-1.5 uppercase tracking-wider">Nombre Completo</label>
                <input required type="text" value={formData.name} onChange={e => setFormData({...formData, name: e.target.value})} className="w-full bg-[var(--background)] border border-[var(--border)] rounded-lg px-4 py-2.5 text-[14px] text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)] focus:ring-1 focus:ring-[var(--accent)] transition-shadow" placeholder="Ej. Dr. Juan Pérez" />
              </div>

              <div>
                <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-1.5 uppercase tracking-wider">Rol / Especialidad</label>
                <input required type="text" value={formData.role} onChange={e => setFormData({...formData, role: e.target.value})} className="w-full bg-[var(--background)] border border-[var(--border)] rounded-lg px-4 py-2.5 text-[14px] text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)] focus:ring-1 focus:ring-[var(--accent)] transition-shadow" placeholder="Ej. Odontólogo General" />
              </div>

              <div className="grid grid-cols-2 gap-5">
                <div>
                  <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-1.5 uppercase tracking-wider">Hora Inicio</label>
                  <input required type="time" value={formData.startTime} onChange={e => setFormData({...formData, startTime: e.target.value})} className="w-full bg-[var(--background)] border border-[var(--border)] rounded-lg px-4 py-2.5 text-[14px] text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)] focus:ring-1 focus:ring-[var(--accent)] transition-shadow pattern-time" />
                </div>
                <div>
                  <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-1.5 uppercase tracking-wider">Hora Fin</label>
                  <input required type="time" value={formData.endTime} onChange={e => setFormData({...formData, endTime: e.target.value})} className="w-full bg-[var(--background)] border border-[var(--border)] rounded-lg px-4 py-2.5 text-[14px] text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)] focus:ring-1 focus:ring-[var(--accent)] transition-shadow pattern-time" />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-5 items-end">
                <div>
                  <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-1.5 uppercase tracking-wider">Duración Turno</label>
                  <select required value={formData.slotDurationMinutes} onChange={e => setFormData({...formData, slotDurationMinutes: parseInt(e.target.value)})} className="w-full bg-[var(--background)] border border-[var(--border)] rounded-lg px-4 py-2.5 text-[14px] text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)] focus:ring-1 focus:ring-[var(--accent)] transition-shadow appearance-none cursor-pointer">
                    <option value={15}>15 minutos</option>
                    <option value={20}>20 minutos</option>
                    <option value={30}>30 minutos</option>
                    <option value={45}>45 minutos</option>
                    <option value={60}>1 hora</option>
                    <option value={90}>1.5 horas</option>
                    <option value={120}>2 horas</option>
                  </select>
                </div>
                <div className="pb-2">
                  <label className="flex items-center justify-between p-3 border border-[var(--border)] rounded-lg cursor-pointer hover:bg-[var(--surface-hover)] transition-colors group">
                    <span className="text-[13px] font-bold text-[var(--text-primary)] select-none group-hover:-translate-x-1 transition-transform">En servicio activo</span>
                    <div className="relative flex items-center">
                      <input type="checkbox" id="isAvailable" checked={formData.isAvailable} onChange={e => setFormData({...formData, isAvailable: e.target.checked})} className="sr-only" />
                      <div className={`block w-11 h-6 rounded-full transition-colors ${formData.isAvailable ? 'bg-[var(--accent)]' : 'bg-[var(--border)]'}`}></div>
                      <div className={`absolute left-1 top-1 bg-white w-4 h-4 rounded-full transition-transform ${formData.isAvailable ? 'transform translate-x-5' : ''}`}></div>
                    </div>
                  </label>
                </div>
              </div>

              <div className="pt-2">
                <label className="block text-[11px] font-bold text-[var(--text-secondary)] mb-2.5 uppercase tracking-wider">Días de Atención (Semana)</label>
                <div className="flex flex-wrap gap-2">
                  {DAYS.map(day => {
                    const active = formData.workingDays.includes(day.value);
                    return (
                      <button type="button" key={day.value} onClick={() => toggleDay(day.value)} className={`px-4 py-2 text-[12px] font-bold rounded-lg transition-all focus:outline-none focus:ring-2 focus:ring-[var(--accent)] focus:ring-offset-2 focus:ring-offset-[var(--background)] ${active ? 'bg-[var(--accent)] text-white shadow-[0_4px_10px_rgba(var(--accent-rgb),0.3)] border-transparent' : 'bg-[var(--background)] text-[var(--text-secondary)] border-[var(--border)] border hover:border-[var(--text-primary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-hover)]'}`}>
                        {day.label}
                      </button>
                    );
                  })}
                </div>
                {formData.workingDays.length === 0 && (
                  <p className="text-red-500 text-[11px] mt-2 font-medium">Debe seleccionar al menos un día de trabajo.</p>
                )}
              </div>

              <div className="flex justify-end gap-3 pt-6 border-t border-[var(--border)] mt-8">
                <button type="button" disabled={saving} onClick={() => setShowModal(false)} className="px-5 py-2.5 text-[13px] font-bold text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-hover)] rounded-lg w-full sm:w-auto transition-colors">
                  Cancelar
                </button>
                <button type="submit" disabled={saving || formData.workingDays.length === 0} className="px-6 py-2.5 bg-[var(--text-primary)] text-[var(--background)] text-[13px] font-bold rounded-lg hover:opacity-90 transition-all border border-transparent hover:border-white focus:outline-none focus:ring-2 focus:ring-[var(--text-primary)] focus:ring-offset-2 focus:ring-offset-[var(--background)] disabled:opacity-50 flex items-center justify-center min-w-[140px] shadow-md w-full sm:w-auto">
                  {saving ? (
                     <div className="flex items-center gap-2">
                      <div className="w-4 h-4 rounded-full border-2 border-[var(--background)] border-t-transparent animate-spin"></div>
                      <span>Guardando</span>
                     </div>
                  ) : (
                    editingProvider ? 'Guardar Cambios' : 'Crear Proveedor'
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </DashboardLayout>
  );
};

export default Providers;
