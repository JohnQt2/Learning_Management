import React, { useEffect, useState } from 'react';
import { api, downloadMaterial, readMaterial } from '../api.js';
import { Empty, Field, LoadState, Notice, Panel, Tag, useAction, useLoad } from '../shared.jsx';
import { formatFileSize, formatTime, validateUpload } from '../utils.js';

const extractionLabels = { Extracted: 'Đã trích xuất', NeedsOcr: 'Cần OCR', NoText: 'Không có văn bản', Legacy: 'Chưa trích xuất' };
const fileFormat = material => (material.fileFormat || material.originalFileName.split('.').pop() || '').replace(/^\./, '').toLowerCase();

export function LearningContent({ courseId, token, role, topics, materials, onChange }) {
  const [name, setName] = useState(''), [editingId, setEditingId] = useState(null);
  const [topicId, setTopicId] = useState(String(topics[0]?.id || ''));
  const [file, setFile] = useState(null);
  const [generationMaterialId, setGenerationMaterialId] = useState('');
  const policy = useLoad(signal => role === 'Lecturer' ? api('/api/materials/upload-policy', { token, signal }) : null, [token, role]);
  const action = useAction();
  useEffect(() => { if (!topics.some(topic => String(topic.id) === topicId)) setTopicId(String(topics[0]?.id || '')); }, [topics, topicId]);
  async function saveTopic(event) {
    event.preventDefault();
    await action.run(async () => {
      if (!name.trim()) throw new Error('Nhập tên chủ đề.');
      await api(`/api/courses/${courseId}/topics${editingId ? `/${editingId}` : ''}`, { token, method: editingId ? 'PUT' : 'POST', body: { name: name.trim() } });
      setName(''); setEditingId(null); onChange();
    }, editingId ? 'Đã sửa chủ đề.' : 'Đã thêm chủ đề.');
  }
  async function upload(event) {
    event.preventDefault(); const form = event.currentTarget;
    await action.run(async () => {
      if (!policy.data) throw new Error('Chưa tải được giới hạn upload. Vui lòng thử lại.');
      const error = validateUpload(file, policy.data.maxUploadBytes, policy.data.extensions); if (error) throw new Error(error);
      if (!topicId) throw new Error('Chọn chủ đề cho học liệu.');
      const body = new FormData(); body.append('file', file); body.append('topicId', topicId);
      const material = await api(`/api/courses/${courseId}/materials`, { token, method: 'POST', formData: body });
      setFile(null); form.reset(); onChange(); return material;
    }, material => `Đã tải lên học liệu. ${material.extractionMessage || extractionLabels[material.extractionStatus] || ''}`);
  }
  return <><Panel title="Chủ đề học tập" subtitle="Học liệu và câu hỏi được tổ chức theo từng chủ đề."><Notice {...action}/>{role === 'Lecturer' && <form onSubmit={saveTopic}><fieldset disabled={action.busy} className="inline"><Field label={editingId ? 'Sửa tên chủ đề' : 'Tên chủ đề mới'}><input required maxLength={150} value={name} onChange={event => setName(event.target.value)}/></Field><button className="btn outline">{editingId ? 'Lưu chủ đề' : 'Thêm chủ đề'}</button>{editingId && <button className="btn outline" type="button" onClick={() => { setEditingId(null); setName(''); }}>Hủy sửa</button>}</fieldset></form>}
    <div className="itemlist">{topics.length ? topics.map(topic => <div className="item" key={topic.id}><div><b>{topic.name}</b><small>{materials.filter(material => material.topicId === topic.id).length} học liệu</small></div>{role === 'Lecturer' && <button className="btn outline" disabled={action.busy} onClick={() => { setEditingId(topic.id); setName(topic.name); }}>Sửa chủ đề</button>}</div>) : <Empty>Chưa có chủ đề.{role === 'Lecturer' && ' Thêm chủ đề trước khi tải học liệu hoặc soạn câu hỏi.'}</Empty>}</div>
    {role === 'Lecturer' && topics.length > 0 && <LoadState resource={policy}>{policy.data && <form className="editor" onSubmit={upload}><fieldset className="stack" disabled={action.busy || policy.loading}><h3>Tải lên học liệu</h3><Field label="Chủ đề"><select required value={topicId} onChange={event => setTopicId(event.target.value)}>{topics.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}</select></Field><Field label={`${policy.data.extensions.join(', ')} · Tối đa ${formatFileSize(policy.data.maxUploadBytes)}`}><input required type="file" accept={policy.data.extensions.join(',')} onChange={event => setFile(event.target.files?.[0] || null)}/></Field><p className="muted">TXT và Markdown dùng UTF-8. PDF scan hoặc slide chỉ có ảnh cần OCR; hệ thống sẽ thông báo khi không có văn bản để tạo câu hỏi.</p><button className="btn outline">{action.busy ? 'Đang tải và trích xuất…' : 'Tải lên học liệu'}</button></fieldset></form>}</LoadState>}
  </Panel><Panel title="Học liệu" subtitle="Xem nội dung trích xuất hoặc tải tệp gốc trong khóa học."><MaterialList materials={materials} topics={topics} token={token} role={role} onChange={onChange} onGenerate={material => { setGenerationMaterialId(String(material.id)); document.getElementById('ai-generation')?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }}/></Panel>{role === 'Lecturer' && <AiGeneration token={token} materials={materials} onChange={onChange} materialId={generationMaterialId} onSelectMaterial={setGenerationMaterialId}/>}</>;
}

export function MaterialList({ materials, topics = [], token, role = 'Student', onChange, onGenerate }) {
  const action = useAction();
  const [viewer, setViewer] = useState(null), [deleting, setDeleting] = useState(null), [topicId, setTopicId] = useState('');
  const visible = materials.filter(material => !topicId || String(material.topicId) === topicId);
  async function remove(material) {
    await action.run(async () => {
      await api(`/api/materials/${material.id}`, { token, method: 'DELETE' });
      if (viewer?.material.id === material.id) setViewer(null);
      setDeleting(null); onChange?.();
    }, 'Đã xóa học liệu khỏi danh sách. Câu hỏi và kết quả đã tạo được giữ nguyên.');
  }
  return <><Notice {...action}/>{topics.length > 0 && <div className="filters"><Field label="Lọc học liệu theo chủ đề"><select value={topicId} onChange={event => setTopicId(event.target.value)}><option value="">Tất cả chủ đề ({materials.length})</option>{topics.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}</select></Field></div>}
    <div className="itemlist">{visible.length ? visible.map(material => <article className="material-item" key={material.id}><div className="item"><span className={`file-icon file-${fileFormat(material)}`} aria-label={`Định dạng ${fileFormat(material).toUpperCase()}`}>{fileFormat(material).toUpperCase()}</span><div><b>{material.title}</b><small>{topics.find(topic => topic.id === material.topicId)?.name || `Chủ đề #${material.topicId}`} · {material.originalFileName}</small><small>{formatFileSize(material.sizeBytes)} · Tải lên: {material.uploadedAt ? formatTime(material.uploadedAt) : 'Chưa có dữ liệu'}{material.uploadedById != null && ` · Người tải #${material.uploadedById}`}</small><div><Tag ok={material.extractionStatus === 'Extracted'}>{extractionLabels[material.extractionStatus] || 'Chưa có thông tin trích xuất'}</Tag>{material.extractedCharacterCount > 0 && <small> · {material.extractedCharacterCount.toLocaleString('vi-VN')} ký tự</small>}</div>{material.extractionMessage && <small>{material.extractionMessage}</small>}</div></div><div className="actions material-actions"><button className="btn outline" disabled={action.busy} onClick={() => setViewer({ material, mode: 'text' })}>Xem nội dung trích xuất</button>{fileFormat(material) === 'pdf' && <button className="btn outline" disabled={action.busy} onClick={() => setViewer({ material, mode: 'pdf' })}>Xem PDF gốc</button>}<button className="btn outline" disabled={action.busy} onClick={() => action.run(() => downloadMaterial(material.id, token, material.originalFileName), 'Đã bắt đầu tải tài liệu.')}>Tải về</button>{role === 'Lecturer' && <>{material.canGenerateAI && onGenerate && <button className="btn outline" disabled={action.busy} onClick={() => onGenerate(material)}>Tạo câu hỏi AI</button>}{material.extractionStatus === 'Legacy' && <button className="btn outline" disabled={action.busy} onClick={() => action.run(async () => { const updated = await api(`/api/materials/${material.id}/extract`, { token, method: 'POST' }); onChange?.(); if (viewer?.material.id === material.id) setViewer(null); return updated; }, updated => updated.extractionMessage || 'Đã cập nhật nội dung trích xuất.')}>Trích xuất nội dung</button>}<button className="btn danger" disabled={action.busy} onClick={() => setDeleting(material)}>Xóa học liệu</button></>}</div></article>) : <Empty>{materials.length ? 'Không có học liệu trong chủ đề đã chọn.' : 'Chưa có học liệu.'}</Empty>}</div>
    {deleting && <section className="editor" aria-label="Xác nhận xóa học liệu"><h3>Xóa “{deleting.title}”?</h3><p className="muted">Học liệu sẽ được ẩn. Câu hỏi, bài kiểm tra và kết quả đã tạo được giữ nguyên.</p><div className="actions"><button className="btn danger" disabled={action.busy} onClick={() => remove(deleting)}>Xác nhận xóa</button><button className="btn outline" disabled={action.busy} onClick={() => setDeleting(null)}>Giữ học liệu</button></div></section>}
    {viewer && <MaterialPreview key={`${viewer.material.id}-${viewer.mode}`} token={token} material={viewer.material} mode={viewer.mode} onClose={() => setViewer(null)}/>}</>;
}

function MaterialPreview({ token, material, mode, onClose }) {
  const content = useLoad(signal => mode === 'pdf' ? readMaterial(material.id, token, signal) : api(`/api/materials/${material.id}/extracted-text`, { token, signal }), [token, material.id, mode]);
  useEffect(() => () => { if (content.data?.url) URL.revokeObjectURL(content.data.url); }, [content.data]);
  return <section className="material-viewer"><div className="question-top"><h3>{material.title} · {mode === 'pdf' ? 'PDF gốc' : 'Nội dung trích xuất'}</h3><button className="btn outline" onClick={onClose}>Đóng tài liệu</button></div><LoadState resource={content}>{content.data && (mode === 'pdf' && content.data.kind === 'pdf' ? <object className="pdf-viewer" type="application/pdf" data={content.data.url} aria-label={material.title}><p>Trình duyệt chưa hỗ trợ xem PDF. Hãy sử dụng nút Tải về.</p></object> : <>{content.data.message && <p className="muted">{content.data.message}</p>}{content.data.truncated && <p className="alert info">Đang xem một phần của {content.data.characterCount.toLocaleString('vi-VN')} ký tự. Tải tệp gốc để xem đầy đủ.</p>}{content.data.text ? <pre className="text-viewer">{content.data.text}</pre> : <Empty>{extractionLabels[content.data.status] || 'Chưa có nội dung trích xuất.'} PDF scan và trang chiếu chỉ có ảnh cần OCR; tính năng OCR chưa được triển khai.</Empty>}</>)}</LoadState></section>;
}

function AiGeneration({ token, materials, onChange, materialId, onSelectMaterial }) {
  const status = useLoad(signal => api('/api/ai/questions/status', { token, signal }), [token]);
  const [count, setCount] = useState(3), [difficulty, setDifficulty] = useState('Medium');
  const action = useAction();
  const eligible = materials.filter(material => material.canGenerateAI);
  const isGemini = status.data?.enabled && status.data?.mode?.toLowerCase() === 'gemini';
  const selected = eligible.some(material => String(material.id) === materialId) ? materialId : String(eligible[0]?.id || '');
  const minimum = status.data?.minQuestionCount || 3, maximum = status.data?.maxQuestionCount || 10;
  const validCount = Number.isInteger(Number(count)) && Number(count) >= minimum && Number(count) <= maximum;
  const plan = useLoad(signal => isGemini && selected && validCount ? api('/api/ai/questions/plan', { token, method: 'POST', body: { materialId: Number(selected), count: Number(count), difficulty }, signal }) : null, [token, isGemini, selected, count, difficulty, validCount]);
  const planReady = !plan.loading && !plan.error && plan.data?.materialId === Number(selected) && plan.data.chunks?.reduce((total, chunk) => total + chunk.questionCount, 0) === Number(count);
  async function generate(event) {
    event.preventDefault();
    await action.run(async () => {
      if (!planReady) throw new Error('Chờ kế hoạch xử lý hợp lệ trước khi sinh câu hỏi.');
      const data = await api('/api/ai/questions/generate', { token, method: 'POST', body: { materialId: Number(selected), count: Number(count), difficulty } });
      onChange(); return data;
    }, data => `Đã lưu ${data.length} câu hỏi Draft từ Gemini. Mở Ngân hàng câu hỏi để kiểm tra, sửa và duyệt.`);
  }
  return <Panel id="ai-generation" title="Sinh câu hỏi bằng Gemini AI" subtitle="Nội dung đã trích xuất chỉ được gửi khi bạn bấm sinh câu hỏi. Câu hỏi được lưu nháp để giảng viên duyệt."><Notice {...action}/><LoadState resource={status}>{status.data && <>{!isGemini ? <div className="empty">{status.data.message || 'Gemini AI chưa được cấu hình. Bạn vẫn có thể soạn câu hỏi thủ công.'}</div> : <><p className="muted">{status.data.model && `Model: ${status.data.model}`}{status.data.maxSourceCharacters && ` · Giới hạn mỗi đoạn: ${status.data.maxSourceCharacters.toLocaleString('vi-VN')} ký tự`}</p>{eligible.length ? <form onSubmit={generate}><fieldset disabled={action.busy} className="stack"><Field label="Học liệu có nội dung hợp lệ"><select required value={selected} onChange={event => onSelectMaterial(event.target.value)}>{eligible.map(material => <option key={material.id} value={material.id}>{material.title} · {fileFormat(material).toUpperCase()}</option>)}</select></Field><div className="formgrid"><Field label={`Số câu hỏi (${minimum}–${maximum})`}><input required type="number" min={minimum} max={maximum} value={count} onChange={event => setCount(event.target.value)}/></Field><Field label="Độ khó"><select value={difficulty} onChange={event => setDifficulty(event.target.value)}><option value="Easy">Dễ</option><option value="Medium">Trung bình</option><option value="Hard">Khó</option></select></Field></div><GenerationPlan resource={plan}/><p className="muted">Nội dung được gửi đến Gemini theo cấu hình của đơn vị. Quota hoặc chi phí phụ thuộc tài khoản dịch vụ. Chỉ gửi tài liệu bạn được phép sử dụng.</p><button className="btn primary" disabled={!planReady}>{action.busy ? 'Gemini đang sinh câu hỏi…' : 'Sinh câu hỏi từ tài liệu'}</button></fieldset></form> : <Empty>Tải lên học liệu có văn bản trích xuất hợp lệ để sinh câu hỏi. Tài liệu cần OCR hoặc không có văn bản vẫn có thể tải xuống.</Empty>}</>}</>}</LoadState></Panel>;
}

function GenerationPlan({ resource }) {
  return <LoadState resource={resource}>{!resource.loading && !resource.error && resource.data && <section className="generation-plan" aria-label="Kế hoạch tạo câu hỏi"><h3>Kế hoạch xử lý toàn bộ tài liệu</h3><p>{resource.data.totalSourceCharacters.toLocaleString('vi-VN')} ký tự · {resource.data.chunkCount} đoạn · Tối đa {resource.data.providerRequestLimit} lượt gọi Gemini.</p><ol>{resource.data.chunks.map(chunk => <li key={chunk.index}>Đoạn {chunk.index}: ký tự {(chunk.startCharacter + 1).toLocaleString('vi-VN')}–{chunk.endCharacter.toLocaleString('vi-VN')} · {chunk.questionCount} câu hỏi</li>)}</ol><p className="muted">Backend thử lại tối đa {resource.data.maxRetries} lần cho lỗi tạm thời trong ngân sách lượt gọi, có chờ theo Retry-After. Thời gian xử lý tối đa {resource.data.generationTimeoutSeconds} giây. Kế hoạch này chưa gửi nội dung tới Gemini.</p></section>}</LoadState>;
}



