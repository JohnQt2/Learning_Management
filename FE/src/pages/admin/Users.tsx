import { useQuery } from '@tanstack/react-query';
import { getUsers } from '../../services/lms';
import { Empty, ErrorBox, Loading } from '../../components/State';

const roleText = { student: 'Sinh viên', lecturer: 'Giảng viên', admin: 'Quản trị viên' } as const;

export default function Users() {
  const { data, isLoading, isError } = useQuery({ queryKey: ['users'], queryFn: getUsers });

  return (
    <>
      <h1>Người dùng</h1>
      {isLoading && <Loading />}
      {isError && <ErrorBox />}
      {data && data.length === 0 && <Empty text="Chưa có người dùng nào." />}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Họ tên</th><th>Email</th><th>Vai trò</th></tr>
            </thead>
            <tbody>
              {data.map((u) => (
                <tr key={u.id}>
                  <td>{u.name}</td>
                  <td>{u.email}</td>
                  <td>{roleText[u.role]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
