import React, { useState, useEffect, useMemo } from 'react';
import axios from 'axios';
import { format, addDays, startOfWeek, parseISO, isSameDay, addWeeks, subWeeks } from 'date-fns';
import { es } from 'date-fns/locale';
import { ChevronLeft, ChevronRight, Calendar as CalendarIcon, Clock, User } from 'lucide-react';
import DashboardLayout from '../components/DashboardLayout';
import Topbar from '../components/Topbar';

const START_HOUR = 8; // 08:00
const END_HOUR = 20; // 20:00
const MINUTE_STEP = 30; // 30 min slots

const Bookings = () => {
  const [bookings, setBookings] = useState([]);
  const [providers, setProviders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [currentDate, setCurrentDate] = useState(new Date());
  const [draggingBooking, setDraggingBooking] = useState(null);
  const [rescheduling, setRescheduling] = useState(false);
  
  // Modal states
  const [confirmModal, setConfirmModal] = useState({
    isOpen: false,
    booking: null,
    newDate: null,
    newTime: null,
    providerId: null
  });

  useEffect(() => {
    fetchData();
  }, []);

  const fetchData = async () => {
    try {
      setLoading(true);
      const [bookingsRes, provRes] = await Promise.all([
        axios.get('/api/dashboard/bookings'),
        axios.get('/api/dashboard/providers')
      ]);
      setBookings(bookingsRes.data);
      setProviders(provRes.data);
    } catch (err) {
      // Silently handle or show UI error
    } finally {
      setLoading(false);
    }
  };

  const getProviderColor = (id) => {
    const gradients = ['bg-blue-100 border-blue-400 text-blue-800', 'bg-emerald-100 border-emerald-400 text-emerald-800', 'bg-purple-100 border-purple-400 text-purple-800', 'bg-orange-100 border-orange-400 text-orange-800'];
    const idx = id ? Array.from(id).reduce((acc, char) => acc + char.charCodeAt(0), 0) % gradients.length : 0;
    return gradients[idx];
  };

  // Generate Week Days
  const weekStart = startOfWeek(currentDate, { weekStartsOn: 1 }); // Monday
  const weekDays = Array.from({ length: 7 }).map((_, i) => addDays(weekStart, i));

  // Generate Time Slots
  const timeSlots = [];
  for (let h = START_HOUR; h <= END_HOUR; h++) {
    timeSlots.push(`${h.toString().padStart(2, '0')}:00`);
    if (h !== END_HOUR) timeSlots.push(`${h.toString().padStart(2, '0')}:30`);
  }

  // Handle Drag & Drop
  const handleDragStart = (e, booking) => {
    setDraggingBooking(booking);
    e.dataTransfer.effectAllowed = 'move';
    // Small delay to keep the element visible while dragging
    setTimeout(() => {
      e.target.style.opacity = '0.4';
    }, 0);
  };

  const handleDragEnd = (e) => {
    e.target.style.opacity = '1';
    setDraggingBooking(null);
  };

  const handleDragOver = (e) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
  };

  const handleDrop = (e, date) => {
    e.preventDefault();
    if (!draggingBooking || rescheduling) return;

    // We retrieve the drop coordinates to calculate the new time slot
    const rect = e.currentTarget.getBoundingClientRect();
    const dropY = e.clientY - rect.top;
    
    // Each 30 mins = 64px. We calculate the offset from START_HOUR (8:00)
    const totalMinutesDropped = Math.floor(dropY / (64 / 30));
    const newHour = START_HOUR + Math.floor(totalMinutesDropped / 60);
    const newMinute = (totalMinutesDropped % 60) >= 30 ? 30 : 0;
    
    const formattedTime = `${newHour.toString().padStart(2, '0')}:${newMinute.toString().padStart(2, '0')}:00`;

    // Check if dragging to exact same spot
    const isSameDate = isSameDay(new Date(draggingBooking.scheduledDate), date);
    if (isSameDate && draggingBooking.scheduledTime === formattedTime) {
      setDraggingBooking(null);
      return;
    }

    setConfirmModal({
      isOpen: true,
      booking: draggingBooking,
      newDate: date,
      newTime: formattedTime,
      providerId: draggingBooking.providerId
    });
    setDraggingBooking(null);
  };

  const confirmReschedule = async () => {
    const { booking, newDate, newTime, providerId } = confirmModal;
    setConfirmModal({ isOpen: false, booking: null, newDate: null, newTime: null, providerId: null });
    setRescheduling(true);
    
    const previousBookings = [...bookings];
    const localDateStr = format(newDate, 'yyyy-MM-dd');

    setBookings(prev => prev.map(b => {
      if (b.id === booking.id) {
        return { ...b, scheduledDate: localDateStr, scheduledTime: newTime };
      }
      return b;
    }));

    try {
      await axios.put(`/api/dashboard/bookings/${booking.id}/reschedule`, {
        date: localDateStr,
        time: newTime,
        providerId: providerId
      });
    } catch (err) {
      alert(err.response?.data?.message || 'Error al reprogramar la cita. Verifica disponibilidad.');
      setBookings(previousBookings); // Revert on failure
    } finally {
      setRescheduling(false);
    }
  };

  const getBookingsForDate = (date) => {
    return bookings.filter(b => {
      if (b.status === 2 || b.status === 'Cancelled') return false;
      try {
        const bDate = parseISO(b.scheduledDate);
        return isSameDay(bDate, date);
      } catch (e) { return false; }
    });
  };

  const getCardStyle = (booking) => {
    const provider = providers.find(p => p.id === booking.providerId);
    const durationMins = provider?.slotDurationMinutes || 30; // default 30
    
    // Calc Top Offset
    const [h, m] = booking.scheduledTime.split(':').map(Number);
    const startMinutesOfDay = START_HOUR * 60;
    const bookingMinutesOfDay = h * 60 + m;
    const offsetMinutes = bookingMinutesOfDay - startMinutesOfDay;
    
    // 64px = 30 mins -> 1 min = 64/30 px
    const topPx = (offsetMinutes * (64 / 30));
    const heightPx = (durationMins * (64 / 30));

    return {
      top: `${topPx}px`,
      height: `${heightPx}px`,
      zIndex: draggingBooking?.id === booking.id ? 50 : 10
    };
  };

  // Current time line calculation
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(timer);
  }, []);

  const timeToMinutes = (timeStr) => {
    const [h, m] = timeStr.split(':').map(Number);
    return h * 60 + m;
  };

  const currentTimeTop = useMemo(() => {
    const totalMinutes = now.getHours() * 60 + now.getMinutes();
    const startMinutes = START_HOUR * 60;
    const endMinutes = END_HOUR * 60;
    
    if (totalMinutes < startMinutes || totalMinutes > endMinutes) return null;
    
    const elapsed = totalMinutes - startMinutes;
    // Each 30min slot is 64px high, plus 20px for the pt-5 padding
    return (elapsed / 30) * 64 + 20; 
  }, [now]);

  if (loading) return (
    <DashboardLayout>
      <Topbar title="Calendario" />
      <div className="flex-1 flex items-center justify-center bg-[var(--bg)]">
        <div className="flex flex-col items-center gap-4">
          <div className="animate-spin rounded-full h-10 w-10 border-4 border-[var(--accent)] border-t-transparent"></div>
          <span className="text-sm font-bold text-[var(--accent)] animate-pulse uppercase tracking-widest">Sincronizando Agenda</span>
        </div>
      </div>
    </DashboardLayout>
  );

  return (
    <DashboardLayout>
      <Topbar title="Calendario" />
      <div className="flex-1 flex flex-col overflow-hidden bg-[#F9FAFB] p-4 md:p-8">
        
        {/* Header Controls */}
        <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 mb-8">
          <div>
            <h2 className="text-2xl font-black text-slate-900 font-display flex items-center gap-3">
              <div className="w-10 h-10 bg-[var(--accent)] rounded-xl flex items-center justify-center text-white shadow-lg shadow-[var(--accent)]/20">
                <CalendarIcon size={22} />
              </div>
              {format(weekStart, "MMMM yyyy", { locale: es }).replace(/^\w/, c => c.toUpperCase())}
            </h2>
            <p className="text-sm text-slate-500 font-medium mt-1 ml-13">Visualización semanal de disponibilidad y citas.</p>
          </div>

          <div className="flex items-center gap-3 bg-white p-1.5 rounded-2xl shadow-sm border border-slate-200/60">
            <button onClick={() => setCurrentDate(new Date())} className="px-4 py-2 text-xs font-bold text-slate-600 hover:text-[var(--accent)] hover:bg-[var(--accent)]/5 rounded-xl transition-all uppercase tracking-wider">
              Hoy
            </button>
            <div className="w-px h-6 bg-slate-200 mx-1"></div>
            <div className="flex items-center gap-1">
              <button onClick={() => setCurrentDate(subWeeks(currentDate, 1))} className="p-2 rounded-xl text-slate-400 hover:text-slate-900 hover:bg-slate-50 transition-colors">
                <ChevronLeft size={20} />
              </button>
              <button onClick={() => setCurrentDate(addWeeks(currentDate, 1))} className="p-2 rounded-xl text-slate-400 hover:text-slate-900 hover:bg-slate-50 transition-colors">
                <ChevronRight size={20} />
              </button>
            </div>
          </div>
        </div>

        {/* Legend & Summary */}
        <div className="flex flex-wrap items-center justify-between mb-6 px-1">
          <div className="flex gap-4 overflow-x-auto pb-1 scrollbar-hide items-center">
            <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest">Proveedores Operativos</span>
            <div className="flex gap-3">
              {providers.map(p => (
                <div key={p.id} className="flex items-center gap-2 bg-white px-3 py-1.5 rounded-full border border-slate-200/60 shadow-sm transition-transform hover:scale-105 cursor-default">
                  <div className={`w-2.5 h-2.5 rounded-full border-2 ${getProviderColor(p.id).split(' ')[1]} ${getProviderColor(p.id).split(' ')[0].replace('100', '500')}`}></div>
                  <span className="text-[11px] font-bold text-slate-700">{p.name}</span>
                </div>
              ))}
            </div>
          </div>
          <div className="hidden lg:flex items-center gap-2 text-xs font-bold text-slate-400 bg-slate-100/50 px-3 py-1.5 rounded-lg border border-slate-200">
             <Clock size={14} />
             <span>Huso Horario: Local (UTC-5)</span>
          </div>
        </div>

        {/* Main Calendar Container */}
        <div className="flex-1 overflow-auto bg-white border border-slate-200 rounded-[2rem] shadow-[0_20px_50px_-12px_rgba(0,0,0,0.05)] relative flex flex-col group/calendar">
          
          {/* Header Row (Days) - Glassmorphism Sticky */}
          <div className="grid grid-cols-[80px_1fr_1fr_1fr_1fr_1fr_1fr_1fr] sticky top-0 z-40 bg-white/80 backdrop-blur-md border-b border-slate-200/80">
            <div className="flex items-center justify-center border-r border-slate-100 bg-slate-50/30">
              <Clock size={16} className="text-slate-300" />
            </div>
            {weekDays.map((day, i) => {
              const isToday = isSameDay(day, new Date());
              return (
                <div key={i} className={`py-4 text-center border-r border-slate-100 last:border-r-0 ${isToday ? 'relative after:absolute after:bottom-0 after:left-0 after:right-0 after:h-1 after:bg-[var(--accent)]' : ''}`}>
                  <div className={`text-[10px] font-black uppercase tracking-[0.1em] ${isToday ? 'text-[var(--accent)]' : 'text-slate-400'}`}>
                    {format(day, 'EEE', { locale: es })}
                  </div>
                  <div className={`text-xl font-black mt-1 ${isToday ? 'text-slate-900' : 'text-slate-700'}`}>
                    {format(day, 'd')}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Time Grid Scrollable */}
          <div className="flex-1 min-h-0 relative pt-5">
            
            {/* Current Time Line Indicator */}
            {currentTimeTop !== null && isSameDay(weekStart, startOfWeek(now, { weekStartsOn: 1 })) && (
              <div 
                className="absolute left-[80px] right-0 z-[60] pointer-events-none flex items-center"
                style={{ top: `${currentTimeTop}px` }}
              >
                <div className="w-2.5 h-2.5 bg-red-500 rounded-full -ml-1.25 shadow-[0_0_10px_rgba(239,68,68,0.5)]"></div>
                <div className="flex-1 h-0.5 bg-red-500/60 shadow-[0_1px_2px_rgba(0,0,0,0.1)]"></div>
              </div>
            )}
            
            {/* Absolute positioning container for days */}
            <div className="absolute inset-0 left-[80px] grid grid-cols-7 pt-5 pointer-events-none">
              {weekDays.map((day, dayIdx) => {
                  const dayBookings = getBookingsForDate(day);
                  const isToday = isSameDay(day, new Date());
                  
                  return (
                    <div key={dayIdx} className={`relative h-full border-r border-transparent ${isToday ? 'bg-slate-50/20' : ''} pointer-events-auto`}
                         onDragOver={handleDragOver}
                         onDrop={(e) => handleDrop(e, day)}>
                        {dayBookings.map(booking => {
                            const isDragging = draggingBooking?.id === booking.id;
                            const style = getCardStyle(booking);
                            return (
                               <div 
                                  key={booking.id}
                                  draggable
                                  onDragStart={(e) => handleDragStart(e, booking)}
                                  onDragEnd={handleDragEnd}
                                  style={style}
                                  className={`absolute left-1 right-1 rounded-xl p-2.5 border shadow-sm cursor-grab active:cursor-grabbing hover:shadow-md transition-all text-left overflow-hidden ring-offset-1 hover:ring-2 hover:ring-slate-200 ${getProviderColor(booking.providerId)} ${isDragging ? 'opacity-40 scale-95 blur-[1px]' : 'opacity-100'} ${rescheduling && isDragging ? 'animate-pulse' : ''}`}
                                >
                                  <div className="flex items-center justify-between mb-1">
                                    <span className="text-[10px] items-center flex font-black uppercase tracking-widest opacity-60"><Clock size={10} className="mr-1"/> {booking.scheduledTime.slice(0,5)}</span>
                                    <div className="p-0.5 rounded-md bg-white/40"><User size={10} /></div>
                                  </div>
                                  <div className="text-[12px] font-bold truncate leading-tight">{booking.clientName || 'Cliente'}</div>
                               </div>
                            )
                        })}
                    </div>
                  );
              })}
            </div>


            {timeSlots.map((time, idx) => {
              const isHour = time.endsWith(':00');
              const isLast = idx === timeSlots.length - 1;
              return (
                <div key={time} className="grid grid-cols-[80px_1fr_1fr_1fr_1fr_1fr_1fr_1fr] relative">
                  
                  {/* Time Label Column */}
                  <div className={`relative flex items-start justify-center border-r border-slate-100 bg-slate-50/20 py-4 ${isHour ? 'z-10' : ''}`}>
                    {isHour && (
                      <span className="text-[11px] font-black text-slate-400 tabular-nums absolute -top-2 bg-white px-1.5 rounded-full border border-slate-100 shadow-sm transition-colors hover:text-slate-800">
                        {time}
                      </span>
                    )}
                  </div>

                  {/* Background Grid Days (Empty) */}
                  {weekDays.map((day, dayIdx) => {
                    const isToday = isSameDay(day, new Date());
                    return (
                      <div 
                        key={dayIdx} 
                        className={`relative border-r border-slate-100 min-h-[64px] ${!isLast ? (isHour ? 'border-b border-slate-100' : 'border-b border-dashed border-slate-100') : ''} ${isToday ? 'bg-slate-50/20' : ''}`}
                      >
                      </div>
                    );
                  })}
                </div>
              );
            })}
          </div>
        </div>
        
        <div className="mt-6 flex items-center justify-between px-2">
            <div className="flex items-center gap-4 text-[10px] font-bold text-slate-400 uppercase tracking-widest">
                <div className="flex items-center gap-1.5"><div className="w-2 h-2 rounded-full border border-slate-300"></div> Slot Libre</div>
                <div className="flex items-center gap-1.5"><div className="w-2 h-2 rounded-full bg-slate-300"></div> Slot Reservado</div>
            </div>
            <div className="text-xs font-semibold text-slate-500 italic">
                Sugerencia: Haz clic y arrastra cualquier cita para moverla de horario.
            </div>
        </div>
      </div>

      {/* Confirmation Modal */}
      {confirmModal.isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-sm px-4">
          <div className="bg-white rounded-[24px] shadow-2xl p-6 w-full max-w-[400px] animate-in zoom-in-95 duration-200">
            <h3 className="font-display text-[18px] font-bold text-slate-900 mb-2">Confirmar Reprogramación</h3>
            <p className="text-sm text-slate-600 mb-6 leading-relaxed">
              ¿Estás seguro de que deseas mover la cita de <strong>{confirmModal.booking?.clientName || 'Cliente'}</strong> para el <strong>{confirmModal.newDate ? format(confirmModal.newDate, "d 'de' MMMM", { locale: es }) : ''}</strong> a las <strong>{confirmModal.newTime?.slice(0, 5)}</strong>?
            </p>
            <div className="flex gap-3 justify-end mt-2">
              <button 
                onClick={() => setConfirmModal({ isOpen: false, booking: null, newDate: null, newTime: null, providerId: null })}
                className="px-4 py-2 rounded-xl text-sm font-semibold text-slate-600 hover:bg-slate-100 transition-colors"
              >
                Cancelar
              </button>
              <button 
                onClick={confirmReschedule}
                className="px-5 py-2 flex items-center gap-2 rounded-xl bg-[var(--accent)] hover:bg-[var(--accent-hover)] text-white text-sm font-semibold shadow-md shadow-[var(--accent)]/20 transition-all active:scale-95"
              >
                Confirmar
              </button>
            </div>
          </div>
        </div>
      )}

    </DashboardLayout>
  );
};


export default Bookings;
