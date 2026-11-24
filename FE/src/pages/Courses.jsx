import React, { useEffect, useState } from 'react';
import { api } from '../api.js';
import { Empty, Field, LoadState, Notice, Panel, Tag, useAction, useLoad } from '../shared.jsx';
import { formatTime } from '../utils.js';
import { LearningContent } from './Materials.jsx';
import { QuestionBank } from './Questions.jsx';
import { LecturerQuizzes, StudentQuizzes } from './Quizzes.jsx';
import { CourseResults } from './Results.jsx';
import { CourseStatistics } from './Dashboard.jsx';

export function CourseWorkspace({ token, role, initialCourseId = '', initialTab = 'content' }) {
  const courses = useLoad(signal => api('/api/courses', { token, signal }), [token]);
  const [currentId, setCurrentId] = useState(initialCourseId);
  const [create, setCreate] = useState({ code: '', title: '', description: '' });
  const [joinCode, setJoinCode] = useState('');
  const action = useAction();
  useEffect(() => {
    if (courses.data) setCurrentId(previous => courses.data.some(course => String(course.id) === previous) ? previous : String(courses.data[0]?.id || ''));
  }, [courses.data]);
  async function addCourse(event) {
    event.preventDefault();
    await action.run(async () => {
      if (!create.code.trim() || !create.title.trim()) throw new Error('Nhập mã và tên khóa học.');
      const course = await api('/api/courses', { token, method: 'POST', body: create });
      setCreate({ code: '', title: '', description: '' }); setCurrentId(String(course.id)); courses.reload();
    }, 'Đã tạo khóa học và mã tham gia.');
  }
  async function enroll(event) {
    event.preventDefault();
    await action.run(async () => {
      const enrollment = await api('/api/courses/enroll', { token, method: 'POST', body: { joinCode: joinCode.trim() } });
      setJoinCode(''); setCurrentId(String(enrollment.courseId)); courses.reload();
    }, 'Đã tham gia khóa học.');
  }
  const current = courses.data?.find(course => String(course.id) === currentId);
  return <><Panel title={role === 'Lecturer' ? 'Tạo khóa học' : 'Tham gia khóa học'} subtitle={role === 'Lecturer' ? 'Tạo khóa học, chia sẻ mã tham gia và quản lý nội dung.' : 'Nhập mã tham gia do giảng viên cung cấp.'}><Notice {...action}/>
    {role === 'Lecturer' ? <form onSubmit={addCourse}><fieldset className="formgrid" disabled={action.busy}><Field label="Mã khóa học"><input required maxLength={40} value={create.code} onChange={event => setCreate({ ...create, code: event.target.value })}/></Field><Field label="Tên khóa học"><input required maxLength={150} value={create.title} onChange={event => setCreate({ ...create, title: event.target.value })}/></Field><Field label="Mô tả"><textarea rows={2} maxLength={2000} value={create.description} onChange={event => setCreate({ ...create, description: event.target.value })}/></Field><button className="btn primary">Tạo khóa học</button></fieldset></form> : <form onSubmit={enroll}><fieldset className="inline" disabled={action.busy}><Field label="Mã tham gia"><input required maxLength={40} autoComplete="off" value={joinCode} onChange={event => setJoinCode(event.target.value.toUpperCase())}/></Field><button className="btn primary">Tham gia</button></fieldset></form>}
  </Panel><LoadState resource={courses}>{courses.data && <div className="workspace"><aside className="sidebar" aria-label="Chọn khóa học"><h3>KHÓA HỌC CỦA TÔI ({courses.data.length})</h3>{courses.data.length ? courses.data.map(course => <button className={`course-item ${currentId === String(course.id) ? 'active' : ''}`} key={course.id} onClick={() => setCurrentId(String(course.id))} aria-current={currentId === String(course.id) ? 'true' : undefined}><b>{course.code}</b><span>{course.title}</span><small>{course.enrollmentCount ?? 0} sinh viên</small></button>) : <Empty>{role === 'Lecturer' ? 'Tạo khóa học đầu tiên để bắt đầu.' : 'Bạn chưa tham gia khóa học nào.'}</Empty>}</aside><div className="content-column">{current ? <CoursePage key={current.id} course={current} token={token} role={role} refreshCourses={courses.reload} initialTab={String(current.id) === initialCourseId ? initialTab : 'content'}/> : <Panel title="Không gian khóa học"><Empty>Chọn một khóa học trong danh sách.</Empty></Panel>}</div></div>}</LoadState></>;
}

function CoursePage({ course, token, role, refreshCourses, initialTab }) {
  const [tab, setTab] = useState(role === 'Lecturer' && initialTab === 'statistics' ? 'statistics' : 'content');
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState({ title: course.title, description: course.description || '' });
  const action = useAction();
  const resource = useLoad(async signal => {
    const paths = ['topics', 'materials', 'quizzes', ...(role === 'Lecturer' ? ['questions', 'students', 'attempts'] : [])];
    const data = await Promise.all(paths.map(path => api(`/api/courses/${course.id}/${path}`, { token, signal })));
    return Object.fromEntries(paths.map((path, index) => [path, data[index]]));
  }, [course.id, token, role]);
  async function save(event) {
    event.preventDefault();
    await action.run(async () => {
      if (!form.title.trim()) throw new Error('Nhập tên khóa học.');
      await api(`/api/courses/${course.id}`, { token, method: 'PUT', body: form });
      setEditing(false); refreshCourses();
    }, 'Đã cập nhật khóa học.');
  }
  const tabs = [['content', 'Chủ đề & học liệu'], ...(role === 'Lecturer' ? [['questions', 'Ngân hàng câu hỏi']] : []), ['quizzes', 'Bài kiểm tra'], ...(role === 'Lecturer' ? [['students', 'Sinh viên & kết quả'], ['statistics', 'Thống kê']] : [])];
  return <><div className="course-hero"><div><span className="eyebrow">{course.code} · #{course.id}</span><h2>{course.title}</h2><p>{course.description || 'Chưa có mô tả.'}</p></div>{role === 'Lecturer' && <div className="join-code"><small>MÃ THAM GIA</small><strong>{course.joinCode}</strong><button className="mini" disabled={action.busy} onClick={() => action.run(async () => { if (!navigator.clipboard?.writeText) throw new Error('Trình duyệt không hỗ trợ sao chép. Bạn có thể chọn và chép mã phía trên.'); await navigator.clipboard.writeText(course.joinCode); }, 'Đã sao chép mã tham gia.')}>Sao chép mã</button></div>}</div><Notice {...action}/>
    {role === 'Lecturer' && <div className="course-edit"><button className="btn outline" onClick={() => { setForm({ title: course.title, description: course.description || '' }); setEditing(!editing); }} disabled={action.busy}>{editing ? 'Đóng chỉnh sửa' : 'Sửa thông tin khóa học'}</button>{editing && <form className="editor" onSubmit={save}><fieldset className="stack" disabled={action.busy}><Field label="Tên khóa học"><input required maxLength={150} value={form.title} onChange={event => setForm({ ...form, title: event.target.value })}/></Field><Field label="Mô tả"><textarea rows={3} maxLength={2000} value={form.description} onChange={event => setForm({ ...form, description: event.target.value })}/></Field><button className="btn primary">Lưu thông tin</button></fieldset></form>}</div>}
    <nav className="course-nav" aria-label="Nội dung khóa học">{tabs.map(([key, label]) => <button className={`nav-button ${tab === key ? 'active' : ''}`} key={key} onClick={() => setTab(key)} aria-current={tab === key ? 'page' : undefined}>{label}</button>)}</nav>
    <LoadState resource={resource}>{resource.data && <>{tab === 'content' && <LearningContent courseId={course.id} token={token} role={role} topics={resource.data.topics} materials={resource.data.materials} onChange={resource.reload}/>}
      {tab === 'questions' && role === 'Lecturer' && <QuestionBank courseId={course.id} token={token} topics={resource.data.topics} materials={resource.data.materials} questions={resource.data.questions} onChange={resource.reload}/>}
      {tab === 'quizzes' && (role === 'Lecturer' ? <LecturerQuizzes courseId={course.id} token={token} quizzes={resource.data.quizzes} questions={resource.data.questions} onChange={resource.reload}/> : <StudentQuizzes token={token} quizzes={resource.data.quizzes}/>)}
      {tab === 'statistics' && role === 'Lecturer' && <CourseStatistics courseId={course.id} token={token}/>}
      {tab === 'students' && role === 'Lecturer' && <><Panel title={`Sinh viên đã tham gia (${resource.data.students.length})`} subtitle="Tiến độ tính từ các Quiz đã nộp so với Quiz đã công bố trong khóa học."><div className="itemlist">{resource.data.students.length ? resource.data.students.map(student => <div className="item" key={student.studentId}><div><b>{student.fullName}</b><small>{student.username} · {student.email || 'Chưa có email'}</small><small>Tham gia {formatTime(student.enrolledAt)}</small><small>Quiz đã hoàn thành: {student.completedQuizCount}/{student.publishedQuizCount}{student.publishedQuizCount > 0 && ` · ${student.quizCompletionPercent}%`}</small><small>Điểm trung bình: {student.averageScore == null ? 'Chưa có bài nộp' : `${student.averageScore}/10`}</small></div><div className="stack"><Tag ok={!student.isLocked}>{student.isLocked ? 'Tài khoản đã khóa' : 'Tài khoản hoạt động'}</Tag><Tag ok={student.learningStatus === 'Completed'}>{student.publishedQuizCount === 0 ? 'Chưa có Quiz công bố' : ({ NotStarted: 'Chưa làm Quiz', InProgress: 'Đang hoàn thành Quiz', Completed: 'Đã hoàn thành Quiz' }[student.learningStatus] || 'Chưa có dữ liệu')}</Tag></div></div>) : <Empty>Chưa có sinh viên tham gia.</Empty>}</div></Panel><CourseResults token={token} attempts={resource.data.attempts}/></>}
    </>}</LoadState>
  </>;
}
