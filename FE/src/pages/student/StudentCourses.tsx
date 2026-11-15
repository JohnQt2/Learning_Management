import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getCourses } from '../../services/lms';
import { Empty, ErrorBox, Loading } from '../../components/State';

export default function StudentCourses() {
  const { data, isLoading, isError } = useQuery({ queryKey: ['courses'], queryFn: getCourses });

  return (
    <>
      <h1>Môn học của tôi</h1>
      {isLoading && <Loading />}
      {isError && <ErrorBox />}
      {data && data.length === 0 && <Empty text="Bạn chưa đăng ký môn học nào trong học kỳ này." />}
      <div className="course-list">
        {data?.map((c) => (
          <Link key={c.id} to={`/student/courses/${c.id}`} className="course">
            <span className="course-code">{c.code}</span>
            <strong>{c.name}</strong>
            <span className="muted">{c.lecturer} · {c.semester}</span>
          </Link>
        ))}
      </div>
    </>
  );
}
