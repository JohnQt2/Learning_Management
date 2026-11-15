import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getAssignments, getCourse } from '../../services/lms';
import { Empty, ErrorBox, Loading } from '../../components/State';

const statusText = { open: 'Chưa nộp', submitted: 'Đã nộp', graded: 'Đã chấm' } as const;

export default function CourseDetail() {
  const { id = '' } = useParams();
  const course = useQuery({ queryKey: ['course', id], queryFn: () => getCourse(id) });
  const assignments = useQuery({ queryKey: ['assignments', id], queryFn: () => getAssignments(id) });

  return (
    <>
      <Link to="/student/courses" className="back">← Quay lại danh sách môn</Link>
      <h1>{course.data ? `${course.data.code} · ${course.data.name}` : 'Chi tiết môn học'}</h1>

      <h2>Bài tập</h2>
      {assignments.isLoading && <Loading />}
      {assignments.isError && <ErrorBox />}
      {assignments.data && assignments.data.length === 0 && <Empty text="Môn này chưa có bài tập." />}
      {assignments.data && assignments.data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Bài tập</th><th>Hạn nộp</th><th>Trạng thái</th><th>Điểm</th></tr>
            </thead>
            <tbody>
              {assignments.data.map((a) => (
                <tr key={a.id}>
                  <td>{a.title}</td>
                  <td>{a.dueDate}</td>
                  <td><span className={`badge badge-${a.status}`}>{statusText[a.status]}</span></td>
                  <td>{a.score ?? '–'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
