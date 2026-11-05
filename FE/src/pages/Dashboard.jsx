import React, { useState } from 'react';
import { api } from '../api.js';
import { Empty, LoadState, Panel, Tag, useLoad } from '../shared.jsx';
import { formatScore, formatTime } from '../utils.js';
import { AttemptList, RecommendationList, ResultDetail } from './Results.jsx';

function Metrics({ items }) {
  return <dl className="metrics">{items.map(([label, value, detail]) => <div className="metric" key={label}><dt>{label}</dt><dd>{value}{detail && <small>{detail}</small>}</dd></div>)}</dl>;
}

export function StudentDashboard({ token, onOpenCourse }) {
  const dashboard = useLoad(signal => api('/api/students/me/dashboard', { token, signal }), [token]);
  const [attemptId, setAttemptId] = useState(null);
  return <LoadState resource={dashboard}>{dashboard.data && <>
    <Panel title="Tổng quan học tập" subtitle="Tiến độ dựa trên Quiz đã nộp trong các khóa học bạn đang tham gia."><button className="btn outline refresh" disabled={dashboard.loading} onClick={dashboard.reload}>Cập nhật</button><Metrics items={[
      ['Khóa học đã tham gia', dashboard.data.enrolledCourseCount],
      ['Quiz đã hoàn thành', `${dashboard.data.completedQuizCount}/${dashboard.data.publishedQuizCount}`],
      ['Điểm trung bình', formatScore(dashboard.data.averageScore)],
      ['Tiến độ Quiz', dashboard.data.publishedQuizCount ? `${dashboard.data.quizCompletionPercent}%` : 'Chưa có Quiz'],
    ]}/></Panel>
    <Panel title="Tiến độ theo khóa học" subtitle="Tỷ lệ hoàn thành Quiz đã công bố; chưa đo thời gian đọc học liệu."><div className="itemlist">{dashboard.data.courseProgress.length ? dashboard.data.courseProgress.map(course => <article className="item progress-item" key={course.courseId}><div><b>{course.code} · {course.title}</b><small>{course.completedQuizCount}/{course.publishedQuizCount} Quiz hoàn thành · Điểm trung bình: {formatScore(course.averageScore)}</small>{course.publishedQuizCount ? <><progress max="100" value={course.quizCompletionPercent} aria-label={`Tiến độ Quiz khóa ${course.title}`}/><small>{course.quizCompletionPercent}% hoàn thành</small></> : <small>Giảng viên chưa công bố Quiz.</small>}</div><button className="btn outline" onClick={() => onOpenCourse(course.courseId)}>Mở khóa học</button></article>) : <Empty>Bạn chưa tham gia khóa học nào. Mở Khóa học và nhập mã tham gia từ giảng viên.</Empty>}</div></Panel>
    <Panel title="Bài làm gần đây" subtitle="Tối đa 10 bài nộp gần nhất."><AttemptList attempts={dashboard.data.recentAttempts} selected={attemptId} select={setAttemptId}/></Panel>
    {attemptId && <ResultDetail key={attemptId} token={token} attemptId={attemptId}/>}
    <Panel title="Chủ đề cần ôn tập" subtitle="Gợi ý theo quy tắc: tỷ lệ trả lời đúng dưới 60% từ các bài đã nộp."><div className="itemlist">{dashboard.data.weakTopics.length ? dashboard.data.weakTopics.map(topic => <div className="item" key={`${topic.courseId}-${topic.topicId}`}><div><b>{topic.topicName}</b><small>{topic.courseTitle}</small><small>{topic.reason}</small><small>Đúng {topic.correctAnswerCount}/{topic.totalAnswerCount} câu</small></div><Tag ok={false}>{topic.accuracy}% đúng</Tag></div>) : <Empty>Chưa có chủ đề cần ôn tập hoặc chưa có bài nộp.</Empty>}</div></Panel>
    <Panel title="Học liệu gợi ý" subtitle="Chọn từ các chủ đề cần ôn tập theo kết quả đã lưu. Đây là gợi ý theo quy tắc nghiệp vụ."><RecommendationList items={dashboard.data.recommendations} token={token}/></Panel>
  </>}</LoadState>;
}

export function LecturerDashboard({ token, onOpenCourse }) {
  const courses = useLoad(signal => api('/api/courses', { token, signal }), [token]);
  return <Panel title="Tổng quan giảng dạy" subtitle="Các khóa học do bạn quản lý và lượt tham gia thực tế."><LoadState resource={courses}>{courses.data && <><button className="btn outline refresh" disabled={courses.loading} onClick={courses.reload}>Cập nhật</button><Metrics items={[
    ['Khóa học đang quản lý', courses.data.length],
    ['Lượt tham gia', courses.data.reduce((total, course) => total + (course.enrollmentCount ?? 0), 0), 'Một sinh viên có thể tham gia nhiều khóa học.'],
  ]}/><div className="itemlist">{courses.data.length ? courses.data.map(course => <div className="item" key={course.id}><div><b>{course.code} · {course.title}</b><small>{course.enrollmentCount} sinh viên tham gia</small></div><div className="actions"><button className="btn outline" onClick={() => onOpenCourse(course.id)}>Quản lý nội dung</button><button className="btn primary" onClick={() => onOpenCourse(course.id, 'statistics')}>Xem thống kê</button></div></div>) : <Empty>Bạn chưa quản lý khóa học nào. Mở Khóa học để tạo khóa học đầu tiên.</Empty>}</div></>}</LoadState></Panel>;
}

export function CourseStatistics({ token, courseId }) {
  const statistics = useLoad(signal => api(`/api/courses/${courseId}/statistics`, { token, signal }), [token, courseId]);
  return <Panel title="Thống kê khóa học" subtitle="Thống kê từ bài đã nộp của học viên đang tham gia khóa học. Tiến độ tính theo Quiz đã công bố."><LoadState resource={statistics}>{statistics.data && <><button className="btn outline refresh" disabled={statistics.loading} onClick={statistics.reload}>Cập nhật thống kê</button><Metrics items={[
    ['Sinh viên', statistics.data.studentCount],
    ['Quiz đã công bố', statistics.data.publishedQuizCount],
    ['Bài đã nộp', statistics.data.submittedAttemptCount],
    ['Điểm trung bình', formatScore(statistics.data.averageScore)],
  ]}/>
    <h3>Kết quả theo Quiz</h3>{statistics.data.quizStatistics.length ? <div className="table-wrap"><table><caption className="sr-only">Số học viên đã nộp và điểm trung bình từng Quiz</caption><thead><tr><th scope="col">Quiz</th><th scope="col">Công bố</th><th scope="col">Sinh viên đã nộp</th><th scope="col">Điểm trung bình</th></tr></thead><tbody>{statistics.data.quizStatistics.map(quiz => <tr key={quiz.quizId}><th scope="row">{quiz.title}</th><td>{formatTime(quiz.publishedAt)}</td><td>{quiz.submittedStudentCount}/{statistics.data.studentCount}</td><td>{formatScore(quiz.averageScore)}</td></tr>)}</tbody></table></div> : <Empty>Chưa có Quiz đã công bố.</Empty>}
    <h3>Kết quả từng sinh viên</h3>{statistics.data.studentStatistics.length ? <div className="table-wrap"><table><caption className="sr-only">Tiến độ Quiz và điểm trung bình từng sinh viên</caption><thead><tr><th scope="col">Sinh viên</th><th scope="col">Quiz hoàn thành</th><th scope="col">Tiến độ</th><th scope="col">Điểm trung bình</th></tr></thead><tbody>{statistics.data.studentStatistics.map(student => <tr key={student.studentId}><th scope="row">{student.fullName}</th><td>{student.completedQuizCount}/{student.publishedQuizCount}</td><td>{student.publishedQuizCount ? `${student.quizCompletionPercent}%` : 'Chưa có Quiz'}</td><td>{formatScore(student.averageScore)}</td></tr>)}</tbody></table></div> : <Empty>Chưa có sinh viên tham gia.</Empty>}
    <h3>Chủ đề cần củng cố</h3><div className="itemlist">{statistics.data.weakTopics.length ? statistics.data.weakTopics.map(topic => <div className="item" key={topic.topicId}><div><b>{topic.topicName}</b><small>{topic.reason}</small><small>{topic.incorrectAnswerCount}/{topic.totalAnswerCount} câu trả lời sai · {topic.affectedStudentCount} sinh viên trả lời sai</small></div><Tag ok={false}>{topic.accuracy}% đúng</Tag></div>) : <Empty>Chưa có chủ đề có tỷ lệ đúng dưới 60% hoặc chưa có bài nộp.</Empty>}</div>
  </>}</LoadState></Panel>;
}
