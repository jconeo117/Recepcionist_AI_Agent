import React, { useState, useEffect, useRef } from 'react';
import axios from 'axios';
import { useAuth } from '../context/AuthContext';
import { format, formatDistanceToNow } from 'date-fns';
import { es } from 'date-fns/locale';
import { 
  LogOut, AlertCircle, CheckCircle2, Search, Send,
  User, MessageSquare, Clock, ShieldAlert, Bot
} from 'lucide-react';

const Inbox = () => {
  const { logout, tenant } = useAuth();
  const [sessions, setSessions] = useState([]);
  const [activeSession, setActiveSession] = useState(null);
  const [history, setHistory] = useState([]);
  const [replyMessage, setReplyMessage] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const messagesEndRef = useRef(null);

  // Poll sessions periodically
  useEffect(() => {
    fetchSessions();
    const intervalId = setInterval(fetchSessions, 10000); // refresh every 10s
    return () => clearInterval(intervalId);
  }, []);

  // Fetch history when active session changes
  useEffect(() => {
    if (activeSession) {
      fetchHistory(activeSession.id);
      
      const intervalId = setInterval(() => {
        fetchHistory(activeSession.id);
      }, 5000); // refresh history every 5s while chatting
      
      return () => clearInterval(intervalId);
    } else {
      setHistory([]);
    }
  }, [activeSession]);

  // Scroll to bottom of chat
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [history]);

  const fetchSessions = async () => {
    try {
      const res = await axios.get('/api/dashboard/sessions');
      // Sort by NeedsHumanAttention first, then by updated date
      const sorted = res.data.sort((a, b) => {
        if (a.needsHumanAttention === b.needsHumanAttention) {
          return new Date(b.updatedAt) - new Date(a.updatedAt);
        }
        return a.needsHumanAttention ? -1 : 1;
      });
      setSessions(sorted);
      
      // Update active session ref if it exists in new data so NeedsHumanAttention updates visually
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
    e.preventDefault();
    if (!replyMessage.trim() || !activeSession) return;
    
    setIsSending(true);
    try {
      await axios.post(`/api/dashboard/sessions/${activeSession.id}/reply`, {
        message: replyMessage
      });
      setReplyMessage("");
      await fetchHistory(activeSession.id);
      await fetchSessions(); // Updates NeedsHumanAttention flag locally to false
    } catch (err) {
      console.error('Failed to send reply', err);
      alert('Error al enviar el mensaje. Verifique la consola.');
    } finally {
      setIsSending(false);
    }
  };

  const filteredSessions = sessions.filter(s => 
    s.userPhone?.toLowerCase().includes(searchTerm.toLowerCase()) ||
    s.id.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="h-screen flex flex-col bg-slate-50 dark:bg-slate-950 font-sans mx-auto max-w-[1600px] shadow-2xl overflow-hidden">
      
      {/* Top Navbar */}
      <header className="h-16 px-6 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between z-10">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 bg-blue-600 rounded-xl flex items-center justify-center text-white shadow-sm">
            <MessageSquare className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-lg font-bold text-slate-800 dark:text-slate-100 leading-tight">Bandeja de Entrada</h1>
            <p className="text-xs font-medium text-slate-500 dark:text-slate-400">
              {tenant?.name || 'Cargando organizacion...'}
            </p>
          </div>
        </div>
        
        <button 
          onClick={logout}
          className="flex items-center gap-2 text-sm font-medium text-slate-600 dark:text-slate-300 hover:text-red-500 transition-colors px-3 py-2 rounded-lg hover:bg-red-50 dark:hover:bg-red-950/30"
        >
          <LogOut className="w-4 h-4" />
          Cerrar Sesión
        </button>
      </header>

      {/* Main Layout */}
      <div className="flex-1 flex overflow-hidden">
        
        {/* Sidebar / Session List */}
        <aside className="w-80 lg:w-96 flex flex-col bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800">
          <div className="p-4 border-b border-slate-200 dark:border-slate-800">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input 
                type="text"
                placeholder="Buscar por teléfono o ID..."
                className="input-field pl-9 text-sm py-2"
                value={searchTerm}
                onChange={e => setSearchTerm(e.target.value)}
              />
            </div>
          </div>
          
          <div className="flex-1 overflow-y-auto overflow-x-hidden">
            {filteredSessions.length === 0 ? (
              <div className="p-8 text-center text-sm text-slate-500 flex flex-col items-center">
                <CheckCircle2 className="w-8 h-8 text-slate-300 dark:text-slate-600 mb-2" />
                <p>No hay conversaciones activas.</p>
              </div>
            ) : (
              <ul className="divide-y divide-slate-100 dark:divide-slate-800/50">
                {filteredSessions.map(session => {
                  const isActive = activeSession?.id === session.id;
                  
                  return (
                    <li key={session.id}>
                      <button
                        onClick={() => setActiveSession(session)}
                        className={`w-full text-left p-4 hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors flex items-start gap-4 ${isActive ? 'bg-blue-50/50 dark:bg-blue-900/10' : ''}`}
                      >
                        <div className="relative">
                          <div className={`w-12 h-12 rounded-full flex items-center justify-center shrink-0 shadow-sm border ${isActive ? 'bg-blue-100 border-blue-200 text-blue-600 dark:bg-blue-900/30 dark:border-blue-800 dark:text-blue-400' : 'bg-slate-100 border-slate-200 text-slate-600 dark:bg-slate-800 dark:border-slate-700 dark:text-slate-400'}`}>
                            <User className="w-6 h-6" />
                          </div>
                          {session.needsHumanAttention && (
                            <div className="absolute -top-1 -right-1 w-4 h-4 bg-red-500 rounded-full border-2 border-white dark:border-slate-900 animate-pulse" />
                          )}
                        </div>
                        
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center justify-between mb-1">
                            <h3 className="font-semibold text-sm text-slate-900 dark:text-slate-100 truncate">
                              +{session.userPhone}
                            </h3>
                            <span className="text-xs text-slate-500 whitespace-nowrap ml-2">
                              {formatDistanceToNow(new Date(session.updatedAt), { addSuffix: true, locale: es })}
                            </span>
                          </div>
                          
                          <div className="flex items-center justify-between">
                            <p className="text-xs text-slate-500 dark:text-slate-400 truncate pr-2">
                              ID: {session.id.split('-')[0]}...
                            </p>
                            
                            {session.needsHumanAttention && (
                              <span className="shrink-0 inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400 text-[10px] font-bold uppercase tracking-wider">
                                <ShieldAlert className="w-3 h-3" /> Requiere Atención
                              </span>
                            )}
                          </div>
                        </div>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        </aside>

        {/* Chat Area */}
        <main className="flex-1 flex flex-col bg-slate-50 dark:bg-slate-950 relative">
          
          {!activeSession ? (
            <div className="absolute inset-0 flex flex-col items-center justify-center bg-slate-50/80 dark:bg-slate-950/80 backdrop-blur-sm z-10">
              <div className="w-20 h-20 bg-blue-100 dark:bg-blue-900/30 rounded-2xl flex items-center justify-center mb-4 border border-blue-200 dark:border-blue-800/50">
                <MessageSquare className="w-10 h-10 text-blue-500" />
              </div>
              <h2 className="text-xl font-semibold text-slate-800 dark:text-slate-200 mb-2">Ningún chat seleccionado</h2>
              <p className="text-slate-500 dark:text-slate-400 px-4 text-center max-w-sm">
                Seleccione una conversación del panel lateral para revisar el historial completo y enviar mensajes manuales.
              </p>
            </div>
          ) : null}

          {/* Active Chat Header */}
          {activeSession && (
            <div className="h-16 px-6 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between bg-white/80 dark:bg-slate-900/80 backdrop-blur-md sticky top-0 z-10 shrink-0">
              <div className="flex items-center gap-3">
                <h2 className="font-bold text-lg text-slate-800 dark:text-slate-100">
                  Chat con +{activeSession.userPhone}
                </h2>
                {activeSession.needsHumanAttention && (
                  <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400 text-xs font-bold uppercase tracking-wider animate-pulse">
                    Escalado
                  </span>
                )}
              </div>
              <div className="flex items-center text-xs text-slate-500 dark:text-slate-400 bg-slate-100 dark:bg-slate-800 px-3 py-1.5 rounded-full font-mono">
                <Clock className="w-3 h-3 mr-1.5" />
                Actualizado: {format(new Date(activeSession.updatedAt), "HH:mm, dd MMM")}
              </div>
            </div>
          )}

          {/* Chat Messages */}
          <div className="flex-1 overflow-y-auto p-4 sm:p-6 space-y-4 md:space-y-6">
            {history.map((msg, index) => {
              // Ignore system prompts to maintain a cleaner view
              if (msg.Role.toLowerCase() === 'system') return null;

              const isUser = msg.Role.toLowerCase() === 'user';
              const isHumanAgent = msg.Role.toLowerCase() === 'assistant' && msg.Content.includes("[Agente Humano]");
              
              let bubbleColor = isUser
                ? "bg-white border-slate-200 text-slate-800 dark:bg-slate-800 dark:border-slate-700 dark:text-slate-100 rounded-br-none" 
                : "bg-blue-600 border-blue-600 text-white rounded-bl-none shadow-md";
              
              if (isHumanAgent) {
                 bubbleColor = "bg-indigo-600 border-indigo-600 text-white rounded-bl-none shadow-md ring-2 ring-indigo-400/20";
              }

              return (
                <div key={index} className={`flex w-full ${isUser ? 'justify-end' : 'justify-start'}`}>
                  {!isUser && (
                    <div className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 mr-3 mt-1 shadow-sm ${isHumanAgent ? 'bg-indigo-100 text-indigo-600' : 'bg-blue-100 text-blue-600'}`}>
                      {isHumanAgent ? <User className="w-4 h-4" /> : <Bot className="w-4 h-4" />}
                    </div>
                  )}
                  
                  <div className={`shadow-sm border px-4 py-3 sm:px-5 sm:py-3.5 rounded-2xl max-w-[85%] lg:max-w-[70%] text-[15px] leading-relaxed relative group ${bubbleColor}`}>
                    
                    {/* Role Badges */}
                    {!isUser && (
                      <div className="flex items-center gap-2 mb-1 opacity-80">
                         <span className="text-[10px] font-bold uppercase tracking-wider text-white/90">
                           {isHumanAgent ? "Agente Humano" : "Asistente Virtual"}
                         </span>
                      </div>
                    )}

                    <p className="whitespace-pre-wrap font-medium">
                      {isHumanAgent ? msg.Content.replace("[Agente Humano]: ", "") : msg.Content}
                    </p>
                  </div>
                </div>
              );
            })}
            <div ref={messagesEndRef} />
          </div>

          {/* Input Area */}
          {activeSession && (
            <div className="p-4 bg-white dark:bg-slate-900 border-t border-slate-200 dark:border-slate-800 shrink-0">
              {activeSession.needsHumanAttention && (
                <div className="mb-3 px-4 py-2 bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-900/50 rounded-lg flex items-start gap-3">
                  <AlertCircle className="w-5 h-5 text-amber-500 shrink-0 mt-0.5" />
                  <div className="text-sm text-amber-800 dark:text-amber-400">
                    <p className="font-semibold">Esta conversacion requiere su atencion.</p>
                    <p className="text-xs opacity-90">Escriba un mensaje a continuacion y presione Enviar para responder y limpiar esta alerta.</p>
                  </div>
                </div>
              )}
              
              <form onSubmit={handleReply} className="flex items-end gap-3 max-w-4xl mx-auto">
                <div className="relative flex-1">
                  <textarea
                    value={replyMessage}
                    onChange={(e) => setReplyMessage(e.target.value)}
                    placeholder="Escriba su respuesta (se enviará por WhatsApp)..."
                    className="input-field min-h-[52px] max-h-[160px] py-3 resize-y block text-sm focus:ring-blue-500 rounded-xl"
                    rows="1"
                    onInput={(e) => {
                      e.target.style.height = 'auto';
                      e.target.style.height = (e.target.scrollHeight) + 'px';
                    }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && !e.shiftKey) {
                        e.preventDefault();
                        if (replyMessage.trim()) handleReply(e);
                      }
                    }}
                  />
                </div>
                
                <button
                  type="submit"
                  disabled={isSending || !replyMessage.trim()}
                  className="btn-primary flex-shrink-0 h-13 px-6 rounded-xl flex items-center justify-center gap-2 group disabled:opacity-60 hover:shadow-md transition-all active:scale-95"
                >
                  {isSending ? (
                    <div className="w-5 h-5 border-2 border-white/40 border-t-white rounded-full animate-spin" />
                  ) : (
                    <>
                      <span className="font-semibold">Enviar</span>
                      <Send className="w-4 h-4 group-hover:translate-x-1 group-hover:-translate-y-1 transition-transform" />
                    </>
                  )}
                </button>
              </form>
            </div>
          )}

        </main>
      </div>
    </div>
  );
};

export default Inbox;
