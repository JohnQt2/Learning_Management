import React, { useState } from 'react';
import { api } from '../api.js';
import { Empty, Field, Notice, Panel, Tag, useAction } from '../shared.jsx';
import { DIFFICULTIES, emptyQuestion, filterQuestions, validateQuestion } from '../utils.js';

export function QuestionBank({ courseId, token, topics, materials = [], questions, onChange }) {
  const [form, setForm] = useState(emptyQuestion), [editingId, setEditingId] = useState(null);
  const [topicId, setTopicId] = useState('');
  const [filters, setFilters] = useState({ search: '', status: '', topicId: '', source: '', difficulty: '' });
  const action = useAction();
  const selectedTopic = topics.some(topic => String(topic.id) === topicId) ? topicId : String(topics[0]?.id || '');
  function cancel() { setEditingId(null); setForm(emptyQuestion()); }
  function edit(question) {
    setEditingId(question.id); setTopicId(String(question.topicId));
    setForm({ content: question.content, explanation: question.explanation || '', difficulty: question.difficulty || '', options: question.options.map(option => option.content), correct: question.options.findIndex(option => option.isCorrect) });
    document.getElementById('question-editor')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  async function save(event) {
    event.preventDefault();
    await action.run(async () => {
      const error = validateQuestion(form); if (error) throw new Error(error);
      if (!selectedTopic) throw new Error('Tạo và chọn chủ đề trước khi soạn câu hỏi.');
      const body = { content: form.content.trim(), explanation: form.explanation.trim(), difficulty: form.difficulty, options: form.options.map((content, index) => ({ content: content.trim(), isCorrect: index === form.correct })), ...(!editingId ? { courseId, topicId: Number(selectedTopic) } : {}) };
      await api(editingId ? `/api/questions/${editingId}` : '/api/questions', { token, method: editingId ? 'PUT' : 'POST', body });
      cancel(); onChange();
    }, 'Đã lưu câu hỏi Draft. Kiểm tra trước khi duyệt.');
  }
  async function approve(id) {
    await action.run(async () => { await api(`/api/questions/${id}/approve`, { token, method: 'POST' }); if (editingId === id) cancel(); onChange(); }, 'Đã duyệt câu hỏi. Câu hỏi Approved có thể được đưa vào Quiz.');
  }
  const visible = filterQuestions(questions, filters);
  return <Panel title="Ngân hàng câu hỏi" subtitle="Soạn hoặc nhận câu hỏi AI → kiểm tra bản nháp → duyệt → chọn cho bài kiểm tra."><Notice {...action}/>
    {!topics.length ? <Empty>Thêm chủ đề ở mục Chủ đề & học liệu để bắt đầu soạn câu hỏi.</Empty> : <form className="editor" id="question-editor" onSubmit={save}><fieldset className="stack" disabled={action.busy}><h3>{editingId ? `Sửa câu hỏi #${editingId}` : 'Soạn câu hỏi mới'}</h3><div className="formgrid"><Field label="Chủ đề"><select required disabled={!!editingId} value={selectedTopic} onChange={event => setTopicId(event.target.value)}>{topics.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}</select></Field><Field label="Độ khó"><select value={form.difficulty} onChange={event => setForm({ ...form, difficulty: event.target.value })}><option value="">Chưa xác định</option>{Object.entries(DIFFICULTIES).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></Field></div><Field label="Nội dung câu hỏi"><textarea required rows={3} maxLength={4000} value={form.content} onChange={event => setForm({ ...form, content: event.target.value })}/></Field><span className="muted">Nhập đủ 4 phương án và chọn một đáp án đúng.</span>{form.options.map((option, index) => <div className="answer-editor" key={index}><label><input type="radio" name="question-correct" checked={form.correct === index} onChange={() => setForm({ ...form, correct: index })}/>Đúng {String.fromCharCode(65 + index)}</label><input aria-label={`Phương án ${String.fromCharCode(65 + index)}`} required maxLength={2000} value={option} onChange={event => setForm({ ...form, options: form.options.map((value, position) => position === index ? event.target.value : value) })}/></div>)}<Field label="Giải thích đáp án"><textarea rows={2} maxLength={4000} value={form.explanation} onChange={event => setForm({ ...form, explanation: event.target.value })}/></Field><div className="actions"><button className="btn primary">Lưu Draft</button>{editingId && <button className="btn outline" type="button" onClick={cancel}>Hủy sửa</button>}</div></fieldset></form>}
    <div className="question-filters filters"><Field label="Tìm câu hỏi"><input type="search" value={filters.search} onChange={event => setFilters({ ...filters, search: event.target.value })} placeholder="Nội dung câu hỏi"/></Field><Field label="Trạng thái"><select value={filters.status} onChange={event => setFilters({ ...filters, status: event.target.value })}><option value="">Tất cả trạng thái</option><option value="Draft">Draft</option><option value="Approved">Approved</option></select></Field><Field label="Chủ đề"><select value={filters.topicId} onChange={event => setFilters({ ...filters, topicId: event.target.value })}><option value="">Tất cả chủ đề</option>{topics.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}</select></Field><Field label="Nguồn câu hỏi"><select value={filters.source} onChange={event => setFilters({ ...filters, source: event.target.value })}><option value="">Tất cả nguồn</option><option value="Manual">Thủ công</option><option value="AI">AI</option></select></Field><Field label="Độ khó"><select value={filters.difficulty} onChange={event => setFilters({ ...filters, difficulty: event.target.value })}><option value="">Tất cả độ khó</option><option value="Unspecified">Chưa xác định</option>{Object.entries(DIFFICULTIES).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></Field><button className="btn outline" onClick={() => setFilters({ search: '', status: '', topicId: '', source: '', difficulty: '' })}>Xóa bộ lọc</button></div><p className="muted">{visible.length}/{questions.length} câu hỏi trong khóa học.</p>
    {visible.length ? visible.map(question => <article className="question-item" key={question.id}><div className="question-top"><b>#{question.id} · {question.content}</b><Tag ok={question.status === 'Approved'}>{question.source === 'Manual' ? 'Thủ công' : question.source} · {question.status}</Tag></div><small className="muted">{topics.find(topic => topic.id === question.topicId)?.name} · Độ khó: {DIFFICULTIES[question.difficulty] || 'Chưa xác định'}</small>{question.materialId && <p className="source-reference">Nguồn: {materials.find(material => material.id === question.materialId)?.title || `Học liệu #${question.materialId}`}{question.sourceChunkIndex != null && ` · Đoạn ${question.sourceChunkIndex}`}{question.sourceStartCharacter != null && question.sourceEndCharacter != null && ` · Ký tự ${(question.sourceStartCharacter + 1).toLocaleString('vi-VN')}–${question.sourceEndCharacter.toLocaleString('vi-VN')}`}</p>}<ol type="A">{question.options.map(option => <li className={option.isCorrect ? 'correct' : ''} key={option.id}>{option.content}{option.isCorrect && ' ✓ Đáp án đúng'}</li>)}</ol>{question.explanation && <p className="explanation">Giải thích: {question.explanation}</p>}{question.status === 'Draft' && <div className="actions"><button className="btn outline" disabled={action.busy} onClick={() => edit(question)}>Sửa Draft</button><button className="btn outline" disabled={action.busy || editingId === question.id} onClick={() => approve(question.id)}>Duyệt câu hỏi</button></div>}</article>) : <Empty>{questions.length ? 'Không có câu hỏi khớp bộ lọc.' : 'Chưa có câu hỏi. Soạn thủ công hoặc sinh bản nháp bằng Gemini từ học liệu có văn bản.'}</Empty>}
  </Panel>;
}


