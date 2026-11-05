import React, { useState } from 'react';
import { api, downloadMaterial } from '../api.js';
import { Empty, LoadState, Notice, Panel, Tag, useAction, useLoad } from '../shared.jsx';
import { formatTime } from '../utils.js';

export function ResultDetail({ token, attemptId }) {
  const result = useLoad(signal => api(`/api/attempts/${attemptId}/result`, { token, signal }), [token, attemptId]);
  return <Panel title={`Kết quả bài làm #${attemptId}`}><LoadState resource={result}>{result.data && <Result data={result.data} token={token}/>}</LoadState></Panel>;
}

function Result({ data, token }) {
  return <><div className="result"><div className="score">{data.score}<small>/10</small></div><p>Đúng {data.correctCount}/{data.totalQuestions} câu · Sai {data.totalQuestions - data.correctCount} câu · Tỷ lệ đúng {data.totalQuestions ? Math.round(1000 * data.correctCount / data.totalQuestions) / 10 : 0}%</p><h3>Kết quả theo chủ đề</h3><div className="itemlist section-gap">{data.topicAccuracies.map(topic => <div className="item" key={topic.topicId}><b>{topic.topicName}</b><span>{topic.accuracy}%</span><Tag ok={!topic.isWeak}>{topic.isWeak ? 'Cần cải thiện' : 'Đạt từ 60%'}</Tag></div>)}</div></div>
    {!!data.answers?.length && <><h3>Chi tiết câu trả lời</h3>{data.answers.map((answer, index) => <article className="question-item" key={answer.questionId || index}><div className="question-top"><b>Câu {index + 1}. {answer.questionContent}</b><Tag ok={answer.isCorrect}>{answer.isCorrect ? 'Đúng' : 'Sai'}</Tag></div><p>Bạn chọn: {answer.selectedOptionContent || 'Không có lựa chọn'}</p><p className="correct">Đáp án đúng: {answer.correctOptionContent}</p>{answer.explanation && <p className="explanation">Giải thích: {answer.explanation}</p>}</article>)}</>}
    <h3>Học liệu nên ôn tập</h3><RecommendationList items={data.recommendations} token={token}/>
  </>;
}

export function StudentHistory({ token }) {
  const history = useLoad(signal => api('/api/students/me/attempts', { token, signal }), [token]);
  const [selected, setSelected] = useState(null);
  return <><Panel title="Lịch sử bài làm" subtitle="Kết quả đã nộp được lưu trong tài khoản của bạn."><LoadState resource={history}>{history.data && <AttemptList attempts={history.data} selected={selected} select={setSelected}/>}</LoadState></Panel>{selected && <ResultDetail key={selected} token={token} attemptId={selected}/>}</>;
}

export function CourseResults({ token, attempts }) {
  const [selected, setSelected] = useState(null);
  return <><Panel title="Kết quả sinh viên" subtitle="Bài nộp thuộc khóa học bạn quản lý."><AttemptList attempts={attempts} selected={selected} select={setSelected} lecturer/></Panel>{selected && <ResultDetail key={selected} token={token} attemptId={selected}/>}</>;
}

export function AttemptList({ attempts, selected, select, lecturer = false }) {
  return <div className="itemlist">{attempts.length ? attempts.map(attempt => {
    const id = attempt.attemptId || attempt.id;
    return <div className="item" key={id}><div><b>{lecturer ? `${attempt.studentName} · ` : ''}{attempt.quizTitle}</b><small>{formatTime(attempt.submittedAt)}</small></div><Tag>{attempt.score}/10</Tag><button className="btn outline" aria-pressed={selected === id} onClick={() => select(id)}>Xem chi tiết</button></div>;
  }) : <Empty>Chưa có bài làm đã nộp.</Empty>}</div>;
}

export function Recommendations({ token }) {
  const recommendations = useLoad(signal => api('/api/students/me/recommendations', { token, signal }), [token]);
  return <Panel title="Gợi ý ôn tập" subtitle="Gợi ý theo quy tắc nghiệp vụ: tổng hợp câu trả lời theo chủ đề. Tỷ lệ đúng dưới 60% cần cải thiện; học liệu được chọn từ khóa học bạn đã tham gia."><LoadState resource={recommendations}>{recommendations.data && <RecommendationList items={recommendations.data} token={token}/>}</LoadState></Panel>;
}

export function RecommendationList({ items, token }) {
  const action = useAction();
  const groups = Object.values(items.reduce((result, item) => {
    const key = `${item.courseId || ''}-${item.topicId}`;
    result[key] ||= { ...item, materials: [] };
    if (item.materialId && !result[key].materials.some(material => material.materialId === item.materialId)) result[key].materials.push(item);
    return result;
  }, {}));
  return <><Notice {...action}/>{groups.length ? groups.map(group => <div className="question-item" key={`${group.courseId}-${group.topicId}`}><div className="question-top"><b>{group.topicName}</b>{typeof group.accuracy === 'number' && <Tag ok={false}>{group.accuracy}% đúng</Tag>}</div><p><Tag>{group.source === 'RuleBased' || !group.source ? 'Gợi ý theo quy tắc' : group.source}</Tag></p>{group.reason && <p className="muted">{group.reason}</p>}{group.courseId && <p className="muted">Khóa học #{group.courseId}</p>}{group.materials.length ? <div className="itemlist section-gap">{group.materials.map(material => <div className="item" key={material.materialId}><b>{material.materialTitle}</b><button className="btn outline" disabled={action.busy} onClick={() => action.run(() => downloadMaterial(material.materialId, token, material.originalFileName || material.materialTitle), 'Đã bắt đầu tải học liệu ôn tập.')}>Tải học liệu</button></div>)}</div> : <p className="muted">Chưa có học liệu cho chủ đề này. Liên hệ giảng viên để được hướng dẫn thêm.</p>}</div>) : <Empty>Chưa có chủ đề yếu cần ôn thêm hoặc chưa có bài làm. Kết quả mới sẽ cập nhật gợi ý tại đây.</Empty>}</>;
}
