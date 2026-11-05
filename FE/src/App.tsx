import { Navigate, Route, Routes } from 'react-router-dom';
import { roleHome, useAuth } from './auth/AuthContext';
import ProtectedRoute from './auth/ProtectedRoute';
import AppLayout from './layouts/AppLayout';
import Login from './pages/Login';
import StudentCourses from './pages/student/StudentCourses';
import CourseDetail from './pages/student/CourseDetail';
import Grading from './pages/lecturer/Grading';
import Users from './pages/admin/Users';

function HomeRedirect() {
  const { user } = useAuth();
  return <Navigate to={user ? roleHome[user.role] : '/login'} replace />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route element={<ProtectedRoute roles={['student']} />}>
            <Route path="/student/courses" element={<StudentCourses />} />
            <Route path="/student/courses/:id" element={<CourseDetail />} />
          </Route>

          <Route element={<ProtectedRoute roles={['lecturer']} />}>
            <Route path="/lecturer/grading" element={<Grading />} />
          </Route>

          <Route element={<ProtectedRoute roles={['admin']} />}>
            <Route path="/admin/users" element={<Users />} />
          </Route>
        </Route>
      </Route>

      <Route path="*" element={<HomeRedirect />} />
    </Routes>
  );
}
