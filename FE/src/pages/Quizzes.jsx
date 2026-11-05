import React, { useState } from 'react';
import { api } from '../api.js';
import { Empty, Field, LoadState, Notice, Panel, Tag, useAction, useLoad } from '../shared.jsx';
import { formatTime, submissionAnswers } from '../utils.js';
import { ResultDetail } from './Results.jsx';

export function LecturerQuizzes({ courseId, token, quizzes, questions, onChange }) {
  const [create, setCreate] = useState({ title: '', description: '' }), [selectedId, setSelectedId] = useState(null);
  const action = useAction();
  async function add(event) {
    event.preventDefault();
    await action.run(async () => {
      if (!create.title.trim()) throw new Error('Nhập tên bài kiểm tra.');
      const data = await api('/api/quizzes', { token, method: 'POST', body: { courseId, ...create } });
      setCreate({ title: '', description: '' }); setSelectedId(data.id); onChange();
    }, 'Đã tạo Quiz Draft. Chọn câu hỏi Approved và lưu trước khi phát hành.');
  }
  return <><Panel title="Quản lý bài kiểm tra" subtitle="Soạn Draft, chọn các câu hỏi đã duyệt, kiểm tra nội dung rồi phát hành."><Notice {...action}/><form onSubmit={add}><fieldset className="stack" disabled={action.busy}><Field label="Tên bài kiểm tra"><input required maxLength={150} value={create.title} onChange={event => setCreate({ ...create, title: event.target.value })}/></Field><Field label="Mô tả"><textarea maxLength={2000} rows={2} value={create.description} onChange={event => setCreate({ ...create, description: event.target.value })}/></Field><button className="btn outline">Tạo Quiz Draft</button></fieldset></form><div className="itemlist section-gap">{quizzes.length ? quizzes.map(quiz => <div className="item" key={quiz.id}><div><b>{quiz.title}</b><small>#{quiz.id} · {quiz.questionCount ?? 0} câu hỏi · {quiz.status === 'Published' ? formatTime(quiz.publishedAt) : 'Chưa phát hành'}</small></div><Tag ok={quiz.status === 'Published'}>{quiz.status}</Tag><button className="btn outline" disabled={action.busy} onClick={() => setSelectedId(quiz.id)}>{quiz.status === 'Draft' ? 'Soạn Quiz' : 'Xem câu hỏi'}</button></div>) : <Empty>Chưa có bài kiểm tra.</Empty>}</div></Panel>{selectedId && <QuizEditor key={selectedId} id={selectedId} token={token} questions={questions} onChange={onChange} close={() => setSelectedId(null)}/>}</>;
}

function QuizEditor({ id, token, questions, onChange, close }) {
  const quiz = useLoad(signal => api(`/api/quizzes/${id}/authoring`, { token, signal }), [id, token]);
  return <Panel title={`Nội dung Quiz #${id}`}><button className="btn outline" onClick={close}>Đóng Quiz</button><LoadState resource={quiz}>{quiz.data && <AuthoringForm key={quiz.data.id} quiz={quiz.data} token={token} questions={questions} onChange={() => { quiz.reload(); onChange(); }}/>}</LoadState></Panel>;
}

function AuthoringForm({ quiz, token, questions, onChange }) {
  const [form, setForm] = useState({ title: quiz.title, description: quiz.description || '', questionIds: quiz.questionIds || quiz.questions?.map(question => question.id) || [] });
  const action = useAction();
  const approved = questions.filter(question => question.status === 'Approved');
  async function submit(event) {
    event.preventDefault(); const publish = event.nativeEvent.submitter?.value === 'publish';
    await action.run(async () => {
      if (!form.title.trim()) throw new Error('Nhập tên bài kiểm tra.');
      if (form.questionIds.length > 200) throw new Error('Mỗi bài kiểm tra được chọn tối đa 200 câu hỏi.');
      if (publish && !form.questionIds.length) throw new Error('Chọn ít nhất một câu hỏi trước khi phát hành.');
      await api(`/api/quizzes/${quiz.id}`, { token, method: 'PUT', body: form });
      if (publish) await api(`/api/quizzes/${quiz.id}/publish`, { token, method: 'POST', body: { questionIds: form.questionIds } });
      onChange();
    }, publish ? 'Đã phát hành Quiz. Sinh viên trong khóa học có thể làm bài.' : 'Đã lưu Quiz Draft và danh sách câu hỏi.');
  }
  return <div className="section-gap"><Notice {...action}/>{quiz.status === 'Draft' ? <form onSubmit={submit}><fieldset className="stack" disabled={action.busy}><Field label="Tên Quiz"><input required maxLength={150} value={form.title} onChange={event => setForm({ ...form, title: event.target.value })}/></Field><Field label="Mô tả"><textarea rows={2} maxLength={2000} value={form.description} onChange={event => setForm({ ...form, description: event.target.value })}/></Field><h3>Chọn câu hỏi Approved ({form.questionIds.length} đã chọn)</h3>{approved.length ? approved.map(question => <label key={question.id} className="checkboxline"><input type="checkbox" checked={form.questionIds.includes(question.id)} onChange={event => setForm({ ...form, questionIds: event.target.checked ? [...form.questionIds, question.id] : form.questionIds.filter(id => id !== question.id) })}/><span>#{question.id} · {question.content}</span></label>) : <Empty>Duyệt câu hỏi ở Ngân hàng câu hỏi trước khi phát hành Quiz.</Empty>}<div className="actions"><button className="btn outline" type="submit" value="save">Lưu Quiz Draft</button><button className="btn primary" type="submit" value="publish" disabled={!form.questionIds.length}>Phát hành Quiz</button></div></fieldset></form> : <><h3>{quiz.title}</h3><p>{quiz.description}</p><Tag ok>Đã phát hành</Tag></>}
    <h3>{quiz.status === 'Draft' ? 'Câu hỏi đang chọn' : 'Câu hỏi trong bài kiểm tra'}</h3>{(quiz.status === 'Draft' ? approved.filter(question => form.questionIds.includes(question.id)) : quiz.questions || []).map((question, index) => <article className="question-item" key={question.id}><b>{index + 1}. {question.content}</b><ol type="A">{question.options.map(option => <li className={option.isCorrect ? 'correct' : ''} key={option.id}>{option.content}{option.isCorrect && ' ✓'}</li>)}</ol>{question.explanation && <p className="explanation">{question.explanation}</p>}</article>)}
  </div>;
}

export function StudentQuizzes({ token, quizzes }) {
  const attempts = useLoad(signal => api('/api/students/me/attempts', { token, signal }), [token]);
  const [taking, setTaking] = useState(null), [answers, setAnswers] = useState({}), [resultId, setResultId] = useState(null);
  const action = useAction();
  async function open(quizId) {
    await action.run(async () => { const quiz = await api(`/api/quizzes/${quizId}`, { token }); setTaking(quiz); setAnswers({}); setResultId(null); });
  }
  async function submit(event) {
    event.preventDefault();
    await action.run(async () => {
      const payload = submissionAnswers(taking, answers);
      const data = await api(`/api/quizzes/${taking.id}/submit`, { token, method: 'POST', body: { answers: payload } });
      setTaking(null); setAnswers({}); setResultId(data.attemptId); attempts.reload();
    }, 'Nộp bài thành công. Điểm đã được tính và lưu trên máy chủ.');
  }
  return <><Panel title="Bài kiểm tra" subtitle="Mỗi bài được nộp một lần. Chọn đủ đáp án trước khi nộp."><Notice {...action}/><LoadState resource={attempts}>{attempts.data && <div className="itemlist">{quizzes.length ? quizzes.map(quiz => {
    const attempt = attempts.data.find(item => item.quizId === quiz.id);
    return <div className="item" key={quiz.id}><div><b>{quiz.title}</b><small>{quiz.description || `${quiz.questionCount ?? 0} câu hỏi`}</small></div><button className="btn outline" disabled={action.busy || attempts.loading || !!taking} onClick={() => attempt ? (setResultId(attempt.id), setTaking(null)) : open(quiz.id)}>{attempt ? 'Xem kết quả' : taking?.id === quiz.id ? 'Đang làm bài' : 'Làm bài'}</button></div>;
  }) : <Empty>Giảng viên chưa phát hành bài kiểm tra nào.</Empty>}</div>}</LoadState>
    {taking && <form className="stack quiz-take" onSubmit={submit}><fieldset className="stack" disabled={action.busy}><h3>{taking.title}</h3>{taking.description && <p>{taking.description}</p>}<p className="muted">Đã trả lời {Object.keys(answers).length}/{taking.questions.length} câu. Lựa chọn chưa nộp sẽ mất khi đóng bài, rời màn hình hoặc làm mới trang.</p>{taking.questions.map((question, index) => <fieldset className="question-item" key={question.id}><legend>Câu {index + 1}. {question.content}</legend>{question.options.map(option => <label className="checkboxline" key={option.id}><input required type="radio" name={`quiz-question-${question.id}`} checked={answers[question.id] === option.id} onChange={() => setAnswers({ ...answers, [question.id]: option.id })}/><span>{option.content}</span></label>)}</fieldset>)}<div className="actions"><button className="btn primary">Nộp bài</button><button className="btn outline" type="button" onClick={() => { setTaking(null); setAnswers({}); }}>Đóng bài chưa nộp</button></div></fieldset></form>}
  </Panel>{resultId && <ResultDetail key={resultId} token={token} attemptId={resultId}/>}</>;
}
