import { useState } from 'react';
import type { ChatMessage } from '../types';
import { askAi } from '../services/lms';

export default function AiAssistant({ onClose }: { onClose: () => void }) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const send = async () => {
    const text = input.trim();
    if (!text || loading) return;
    const next: ChatMessage[] = [...messages, { role: 'user', content: text }];
    setMessages(next);
    setInput('');
    setError('');
    setLoading(true);
    try {
      const reply = await askAi(next);
      setMessages([...next, { role: 'assistant', content: reply }]);
    } catch {
      setError('Trợ lý AI chưa trả lời được. Bấm "Gửi" để thử lại.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <aside className="ai-panel" aria-label="Trợ lý AI">
      <header className="ai-head">
        <strong>Trợ lý AI</strong>
        <button className="btn-ghost" onClick={onClose} aria-label="Đóng trợ lý AI">Đóng</button>
      </header>

      <div className="ai-body">
        {messages.length === 0 && <p className="state">Hỏi về bài giảng, bài tập hoặc khái niệm bạn chưa rõ.</p>}
        {messages.map((m, i) => (
          <div key={i} className={`msg msg-${m.role}`}>{m.content}</div>
        ))}
        {loading && <div className="msg msg-assistant">Đang soạn câu trả lời…</div>}
        {error && <p className="state state-error" role="alert">{error}</p>}
      </div>

      <footer className="ai-foot">
        <p className="hint">Nội dung do AI tạo, hãy kiểm tra lại với tài liệu môn học.</p>
        <div className="ai-input">
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && send()}
            placeholder="Nhập câu hỏi"
            aria-label="Câu hỏi cho trợ lý AI"
          />
          <button className="btn" onClick={send} disabled={loading || !input.trim()}>Gửi</button>
        </div>
      </footer>
    </aside>
  );
}
