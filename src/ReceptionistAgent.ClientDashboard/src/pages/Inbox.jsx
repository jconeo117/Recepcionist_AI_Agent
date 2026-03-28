import React, { useState, useEffect, useRef } from 'react';
import axios from 'axios';
import { useAuth } from '../context/AuthContext';
import { format } from 'date-fns';
import { Search, MessageSquare, Plus, Send, User } from 'lucide-react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import DashboardLayout from '../components/DashboardLayout';
import Topbar from '../components/Topbar';

const Inbox = () => {
  const { tenant, token } = useAuth();
  const [sessions, setSessions] = useState([]);
  const [activeSession, setActiveSession] = useState(null);
  const [history, setHistory] = useState([]);
  const [replyMessage, setReplyMessage] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const [isTypingMap, setIsTypingMap] = useState({});
  const [showProfile, setShowProfile] = useState(false);
  const [patientProfile, setPatientProfile] = useState(null);
  const [loadingProfile, setLoadingProfile] = useState(false);
  const messagesEndRef = useRef(null);
  const activeSessionRef = useRef(activeSession);
  const connectionRef = useRef(null);

  useEffect(() => {
    activeSessionRef.current = activeSession;
  }, [activeSession]);

  useEffect(() => {
    fetchSessions();
    const connection = new HubConnectionBuilder()
        .withUrl("http://localhost:5083/hubs/dashboard", {
            accessTokenFactory: () => token
        })
        .configureLogging(LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connectionRef.current = connection;
    connection.on("ReceiveSessionUpdate", () => {
        fetchSessions();
        if (activeSessionRef.current) {
            fetchHistory(activeSessionRef.current.id);
        }
    });

    connection.on("NotifyTyping", (sessionId, typing) => {
        setIsTypingMap(prev => ({
            ...prev,
            [sessionId]: typing
        }));
    });

    connection.start()
        .then(() => console.log('🟢 SignalR WebSockets Connected'))
        .catch(err => console.error('🔴 SignalR Connection Error: ', err));

    return () => {
      if (connectionRef.current) connectionRef.current.stop();
    };
  }, []);

  useEffect(() => {
    if (activeSession) {
      fetchHistory(activeSession.id);
    } else {
      setHistory([]);
    }
  }, [activeSession]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [history]);

  const fetchSessions = async () => {
    try {
      const res = await axios.get('/api/dashboard/sessions');
      const sorted = res.data.sort((a, b) => {
        if (a.needsHumanAttention === b.needsHumanAttention) {
          return new Date(b.updatedAt) - new Date(a.updatedAt);
        }
        return a.needsHumanAttention ? -1 : 1;
      });
      setSessions(sorted);
      if (activeSession) {
        const updatedActive = sorted.find(s => s.id === activeSession.id);
        if (updatedActive) setActiveSession(updatedActive);
      }
    } catch (err) {
      console.error('Failed to fetch sessions', err);
    }
  };

  const fetchHistory = async (sessionId) => {
    try {
      const res = await axios.get(`/api/dashboard/sessions/${sessionId}/history`);
      setHistory(res.data);
    } catch (err) {
      console.error('Failed to fetch history', err);
    }
  };

  const handleReply = async (e) => {
    if (e) e.preventDefault();
    if (!replyMessage.trim() || !activeSession) return;
    
    setIsSending(true);
    try {
      await axios.post(`/api/dashboard/sessions/${activeSession.id}/reply`, {
        message: replyMessage
      });
      setReplyMessage("");
      await fetchHistory(activeSession.id);
      await fetchSessions();
    } catch (err) {
      console.error('Failed to send reply', err);
      alert('Error al enviar el mensaje. Verifique la consola.');
    } finally {
      setIsSending(false);
    }
  };

  const fetchPatientProfile = async (phone) => {
    if (!phone) return;
    setLoadingProfile(true);
    try {
      const res = await axios.get(`/api/dashboard/clients/${phone}`);
      setPatientProfile(res.data);
    } catch (err) {
      console.error('Failed to fetch patient profile', err);
    } finally {
      setLoadingProfile(false);
    }
  };

  const handleOpenProfile = () => {
    if (activeSession) {
      fetchPatientProfile(activeSession.userPhone);
      setShowProfile(true);
    }
  };

  const filteredSessions = sessions.filter(s => {
    const search = (searchTerm || "").toLowerCase();
    const phone = (s.userPhone || "").toLowerCase();
    const id = (s.id || "").toLowerCase();
    return phone.includes(search) || id.includes(search);
  });

  const attentionSessions = filteredSessions.filter(s => s.needsHumanAttention);
  const normalSessions = filteredSessions.filter(s => !s.needsHumanAttention);

  const needsAttentionCount = sessions.filter(s => s.needsHumanAttention).length;

  useEffect(() => {
    document.title = needsAttentionCount > 0 ? `(${needsAttentionCount}) Dashboard | Recepcionista` : 'Dashboard | Recepcionista';
  }, [needsAttentionCount]);

  const renderSessionItem = (session) => {
    const isActive = activeSession?.id === session.id;
    const needsAttention = session.needsHumanAttention;
    let bgClass = 'hover:bg-[var(--surface-hover)]';
    
    if (isActive) {
      bgClass = needsAttention ? 'bg-red-50 border-l-4 border-l-red-500' : 'bg-[var(--accent)]/10 border-l-4 border-l-[var(--accent)]';
    } else if (needsAttention) {
      bgClass = 'bg-red-50/50 border-l-4 border-l-red-400 hover:bg-red-50';
    } else {
      bgClass = 'border-l-4 border-l-transparent hover:bg-[var(--surface-hover)]';
    }

    return (
      <div 
        key={session.id}
        onClick={() => setActiveSession(session)}
        className={`p-3.5 border-b border-[var(--border)] cursor-pointer transition-all flex gap-3 items-start ${bgClass}`}
      >
        <div className={`w-10 h-10 rounded-full flex items-center justify-center text-white text-[13px] font-bold shrink-0 shadow-sm ${needsAttention ? 'bg-gradient-to-br from-red-500 to-red-600' : 'bg-gradient-to-br from-[var(--accent)] to-[#7C3AED]'}`}>
          {session.userPhone.slice(-2)}
        </div>
        <div className="flex-1 min-w-0 py-0.5">
          <div className="flex items-center justify-between mb-1">
            <div className={`font-bold text-[14px] truncate ${needsAttention ? 'text-red-600' : 'text-[var(--text-primary)]'}`}>
              +{session.userPhone}
            </div>
            <div className={`text-[11px] font-medium ${needsAttention ? 'text-red-500' : 'text-[var(--text-muted)]'}`}>
              {format(new Date(session.updatedAt), "HH:mm")}
            </div>
          </div>
          <div className="text-[12px] text-[var(--text-secondary)] truncate flex items-center gap-1.5">
            {needsAttention && <span className="w-1.5 h-1.5 bg-red-500 rounded-full animate-pulse"></span>}
            {needsAttention ? <span className="text-red-600 font-medium">Asistencia requerida</span> : `ID: ${session.id.slice(0, 8)}`}
          </div>
        </div>
      </div>
    );
  };

  return (
    <DashboardLayout>
      <Topbar title="Inbox" />
      <div className="flex-1 flex overflow-hidden">
        
        {/* Thread List */}
        <div className="w-[300px] shrink-0 border-r border-[var(--border)] overflow-y-auto bg-[var(--surface)] scrollbar-hide">
          <div className="p-3.5 border-b border-[var(--border)] sticky top-0 bg-[var(--surface)] z-10">
            <div className="relative">
              <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" />
              <input 
                className="w-full pl-8 pr-3 py-2 bg-[var(--bg)] border border-[var(--border)] rounded-lg text-[13px] outline-none focus:border-[var(--accent)] transition-all"
                placeholder="Buscar conversaciones..."
                value={searchTerm}
                onChange={e => setSearchTerm(e.target.value)}
              />
            </div>
          </div>

          <div className="flex-1 overflow-y-auto">
            {attentionSessions.length > 0 && (
              <div>
                <div className="px-3.5 py-1.5 text-[10px] font-bold text-red-600 uppercase tracking-widest bg-red-50/80 border-b border-red-100 flex items-center justify-between sticky top-0 z-10 backdrop-blur-sm shadow-sm">
                  <span className="flex items-center gap-1.5">🚨 Requiere Atención</span>
                  <span className="bg-red-500 text-white px-2 py-0.5 rounded-full text-[9px] shadow-sm">{attentionSessions.length}</span>
                </div>
                {attentionSessions.map(session => renderSessionItem(session))}
              </div>
            )}
            
            {normalSessions.length > 0 && (
              <div>
                <div className="px-3.5 py-1.5 text-[10px] font-bold text-[var(--text-muted)] uppercase tracking-widest bg-[var(--surface-hover)] border-b border-[var(--border)] sticky top-0 z-10 backdrop-blur-sm">
                  💬 Activas
                </div>
                {normalSessions.map(session => renderSessionItem(session))}
              </div>
            )}
            {filteredSessions.length === 0 && (
              <div className="p-8 text-center text-[13px] text-[var(--text-muted)] font-medium">
                No se encontraron conversaciones.
              </div>
            )}
          </div>
        </div>

        {/* Chat Area */}
        <div className="flex-1 flex flex-col overflow-hidden bg-[var(--bg)]">
          {activeSession ? (
            <>
              <div className="p-3.5 px-5 border-b border-[var(--border)] bg-[var(--surface)] flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className={`w-10 h-10 rounded-full flex items-center justify-center text-white text-[14px] font-bold shadow-sm ${activeSession.needsHumanAttention ? 'bg-gradient-to-br from-red-500 to-red-600' : 'bg-gradient-to-br from-[var(--accent)] to-[#7C3AED]'}`}>
                    {activeSession.userPhone.slice(-2)}
                  </div>
                  <div>
                    <div className="font-bold text-[15px] text-[var(--text-primary)]">+{activeSession.userPhone}</div>
                    <div className="flex items-center gap-1.5 text-[12px] font-medium mt-0.5">
                      {activeSession.needsHumanAttention ? (
                        <>
                           <span className="w-2 h-2 bg-red-500 rounded-full animate-pulse"></span>
                           <span className="text-red-500">Esperando respuesta humana</span>
                        </>
                      ) : (
                        <>
                           <span className="w-2 h-2 bg-[var(--green)] rounded-full"></span>
                           <span className="text-[var(--text-secondary)]">Atendido por bot</span>
                        </>
                      )}
                    </div>
                  </div>
                </div>
                <div className="flex gap-2">
                  <button className="w-8 h-8 rounded-lg border border-[var(--border)] flex items-center justify-center text-[var(--text-secondary)] hover:bg-[var(--bg)] transition-colors">
                    <MessageSquare size={14} />
                  </button>
                  <button 
                    onClick={handleOpenProfile}
                    className={`w-8 h-8 rounded-lg border flex items-center justify-center transition-colors ${showProfile ? 'bg-[var(--accent)] text-white border-[var(--accent)]' : 'border-[var(--border)] text-[var(--text-secondary)] hover:bg-[var(--bg)]'}`}
                  >
                    <User size={14} />
                  </button>
                </div>
              </div>

              <div className="flex-1 overflow-y-auto p-5 pb-2 flex flex-col gap-1.5">
                {history.map((msg, i) => {
                  const role = (msg.role || msg.Role || "").toLowerCase();
                  const content = msg.content || msg.Content || "";
                  const timestamp = msg.timestamp || msg.Timestamp || new Date().toISOString();
                  
                  // Render System or Tool messages as logs
                  if (role === 'system' || role === 'tool') {
                    return (
                      <div key={i} className="flex flex-col items-center my-3 px-10">
                        <div className="bg-slate-100/80 backdrop-blur-sm border border-slate-200/50 rounded-full px-4 py-1.5 text-[11px] font-bold text-slate-500 shadow-sm flex items-center gap-2 max-w-full italic">
                          <span className="opacity-70">{format(new Date(timestamp), "HH:mm")}</span>
                          <span className="w-1.5 h-1.5 bg-slate-300 rounded-full"></span>
                          <span className="truncate">{content}</span>
                        </div>
                      </div>
                    );
                  }

                  const isUser = role === 'user';
                  const isHumanAdvisor = role === 'human_advisor';
                  
                  return (
                    <div key={i} className={`flex flex-col max-w-[85%] ${isUser ? 'self-start items-start' : 'self-end items-end'}`}>
                      <div className={`px-4 py-2.5 rounded-[18px] text-[13.5px] leading-relaxed whitespace-pre-wrap break-words 
                        ${isUser 
                          ? 'bg-[var(--surface)] text-[var(--text-primary)] rounded-bl-[4px] shadow-[0_1px_2px_rgba(0,0,0,0.06)] border border-[var(--border)]' 
                          : isHumanAdvisor 
                            ? 'bg-emerald-600 text-white rounded-br-[4px] shadow-sm' 
                            : 'bg-[var(--accent)] text-white rounded-br-[4px] shadow-sm'}`}>
                        {content}
                      </div>
                      <div className="text-[10px] text-[var(--text-muted)] mt-1 px-1 flex items-center gap-1.5 focus:outline-none">
                        {format(new Date(timestamp), "HH:mm")}
                        {!isUser && (
                          <span className={`${isHumanAdvisor ? 'text-emerald-600 font-semibold' : 'opacity-50'}`}>
                            • {isHumanAdvisor ? 'Asesor Humano' : 'Asistente IA'}
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}
                
                {/* Typing Indicator */}
                {activeSession && isTypingMap[activeSession.id] && (
                  <div className="flex flex-col max-w-[68%] self-start items-start">
                    <div className="px-4 py-3 rounded-[16px] rounded-bl-[4px] bg-[var(--surface)] shadow-[0_1px_2px_rgba(0,0,0,0.06)] flex items-center h-[38px]">
                      <div className="flex space-x-1.5">
                        <div className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce [animation-delay:-0.3s]"></div>
                        <div className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce [animation-delay:-0.15s]"></div>
                        <div className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce"></div>
                      </div>
                    </div>
                    <div className="text-[10px] text-[var(--text-muted)] mt-1 px-0.5">La IA está escribiendo...</div>
                  </div>
                )}

                <div ref={messagesEndRef} />
              </div>

              {/* Quick Templates */}
              <div className="px-5 py-2.5 bg-[var(--bg)] border-t border-[var(--border)] flex gap-2 overflow-x-auto scrollbar-hide">
                <span className="text-[11px] font-bold text-slate-400 uppercase tracking-widest flex items-center mr-1 shrink-0">Respuestas Rápidas:</span>
                {[
                  "¡Su cita fue confirmada con éxito!",
                  "Llegaremos en 5 minutos.",
                  "Por favor, envíe su ubicación actual.",
                  "Lo sentimos, no hay disponibilidad ese día."
                ].map((tmpl, idx) => (
                  <button 
                    key={idx}
                    onClick={() => setReplyMessage(tmpl)}
                    className="shrink-0 text-xs font-semibold px-3 py-1.5 bg-white border border-slate-200 text-slate-600 rounded-lg hover:bg-slate-50 hover:text-[var(--accent)] hover:border-[var(--accent)]/30 transition-all"
                  >
                    {tmpl}
                  </button>
                ))}
              </div>

              <div className="p-4 px-5 bg-[var(--surface)] border-t border-[var(--border)] flex items-center gap-2.5">
                <button className="w-[38px] h-[38px] rounded-lg border border-[var(--border)] flex items-center justify-center text-[var(--text-secondary)] hover:bg-[var(--bg)] transition-colors">
                  <Plus size={16} />
                </button>
                <textarea 
                  className="flex-1 bg-[var(--bg)] border-[1.5px] border-[var(--border)] rounded-[22px] px-4 py-2.5 text-[13.5px] outline-none focus:border-[var(--accent)] transition-all resize-none max-h-[100px] overflow-y-auto"
                  placeholder="Escribe un mensaje..."
                  rows="1"
                  value={replyMessage}
                  onChange={e => setReplyMessage(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleReply();
                    }
                  }}
                />
                <button 
                  onClick={() => handleReply()}
                  disabled={isSending || !replyMessage.trim()}
                  className="w-[38px] h-[38px] rounded-full bg-[var(--accent)] text-white flex items-center justify-center cursor-pointer transition-all hover:bg-[var(--accent-hover)] hover:scale-105 active:scale-100 disabled:opacity-50"
                >
                  <Send size={15} className="ml-0.5" />
                </button>
              </div>
            </>
          ) : (
            <div className="flex-1 flex flex-col items-center justify-center text-[var(--text-muted)] gap-2 border-l border-[var(--border)]">
              <MessageSquare size={48} className="opacity-20 mb-2" />
              <div className="font-display text-[15px] font-bold">Selecciona una conversación</div>
              <div className="text-[13px]">Elige un chat de la lista para comenzar</div>
            </div>
          )}
        </div>

        {/* Patient Profile Sidebar (Mini-CRM) */}
        {showProfile && activeSession && (
          <div className="w-[320px] shrink-0 border-l border-[var(--border)] bg-white shadow-xl z-20 overflow-y-auto flex flex-col relative animate-in slide-in-from-right-8 duration-300">
            <div className="p-5 border-b border-[var(--border)] sticky top-0 bg-white/90 backdrop-blur-sm z-10 flex items-center justify-between">
              <h3 className="font-display font-black text-[15px] text-slate-900 tracking-tight flex items-center gap-2">
                <User size={16} className="text-[var(--accent)]" /> Perfil de Paciente
              </h3>
              <button onClick={() => setShowProfile(false)} className="text-slate-400 hover:text-slate-700 transition-colors p-1">
                <Plus size={18} className="rotate-45" />
              </button>
            </div>
            
            <div className="p-5 flex-1">
              <div className="flex flex-col items-center text-center mb-8">
                <div className={`w-16 h-16 rounded-full flex items-center justify-center text-white text-[20px] font-bold shadow-lg bg-gradient-to-br from-[var(--accent)] to-[#7C3AED] mb-3 ring-4 ring-[var(--accent)]/10`}>
                  {activeSession.userPhone.slice(-2)}
                </div>
                <div className="font-black text-[18px] text-slate-900">+{activeSession.userPhone}</div>
                <div className="text-[12px] font-semibold text-slate-500 mt-1 uppercase tracking-widest bg-slate-100 px-3 py-1 rounded-full">Cliente Regular</div>
              </div>

              {loadingProfile ? (
                 <div className="flex justify-center py-8">
                   <div className="animate-spin rounded-full h-8 w-8 border-4 border-[var(--accent)] border-t-transparent"></div>
                 </div>
              ) : patientProfile ? (
                <div className="space-y-6">
                  {/* Bookings History */}
                  <div>
                    <h4 className="text-[11px] font-black text-slate-400 uppercase tracking-widest mb-3 flex items-center justify-between">
                      Historial de Citas
                      <span className="bg-slate-100 text-slate-600 px-2 py-0.5 rounded-md text-[9px]">{patientProfile.bookings.length}</span>
                    </h4>
                    {patientProfile.bookings.length === 0 ? (
                       <div className="text-[13px] text-slate-500 italic p-3 bg-slate-50 rounded-xl border border-slate-100 text-center">Sin citas registradas.</div>
                    ) : (
                       <div className="space-y-2">
                         {patientProfile.bookings.map((booking, idx) => (
                           <div key={idx} className="p-3 bg-white border border-slate-200 rounded-xl shadow-sm hover:border-[var(--accent)]/40 transition-colors">
                             <div className="flex items-center justify-between mb-1.5">
                               <span className="text-[12px] font-bold text-slate-800">{format(new Date(booking.scheduledDate), "MMM d, yyyy")}</span>
                               <span className="text-[10px] font-black text-slate-400">#{booking.confirmationCode.slice(-4)}</span>
                             </div>
                             <div className="text-[11px] font-medium text-slate-500 break-words">{booking.serviceDetails || 'Consulta'}</div>
                             <div className="mt-2 text-[10px] font-bold px-2 py-1 bg-green-50 text-green-700 rounded-md w-fit">
                               {booking.status === 'Confirmed' ? '✅ Confirmada' : booking.status}
                             </div>
                           </div>
                         ))}
                       </div>
                    )}
                  </div>

                  {/* Chat Sessions History */}
                  <div>
                    <h4 className="text-[11px] font-black text-slate-400 uppercase tracking-widest mb-3 flex items-center justify-between">
                      Sesiones Previas
                      <span className="bg-slate-100 text-slate-600 px-2 py-0.5 rounded-md text-[9px]">{patientProfile.sessions.length}</span>
                    </h4>
                    {patientProfile.sessions.length === 0 ? (
                       <div className="text-[13px] text-slate-500 italic p-3 bg-slate-50 rounded-xl border border-slate-100 text-center">Sin historial de chat.</div>
                    ) : (
                       <div className="space-y-2">
                         {patientProfile.sessions.map((sess, idx) => (
                           <div key={idx} className="flex flex-col gap-1 p-3 bg-slate-50 border border-slate-200 rounded-xl">
                             <div className="flex justify-between items-center text-[11px]">
                               <span className="font-bold text-slate-700">{format(new Date(sess.updatedAt), "dd/MM/yyyy")}</span>
                               <span className="text-slate-400 uppercase tracking-wider">{format(new Date(sess.updatedAt), "HH:mm")}</span>
                             </div>
                             <div className="text-[10px] text-slate-500 truncate">Sess. Id: {sess.id.slice(0, 8)}</div>
                             {sess.needsHumanAttention && (
                               <div className="text-[9px] font-bold text-red-600 uppercase mt-1">Requiere Atención</div>
                             )}
                           </div>
                         ))}
                       </div>
                    )}
                  </div>
                </div>
              ) : (
                 <div className="text-center py-8 text-sm text-slate-500">Error cargando perfil.</div>
              )}
            </div>
          </div>
        )}
      </div>
    </DashboardLayout>
  );
};

export default Inbox;
