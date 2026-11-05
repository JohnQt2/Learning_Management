import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getSubmissions, gradeSubmission } from '../../services/lms';
import { Empty, ErrorBox, Loading } from '../../components/State';
import type { Submission } from '../../types';

function Row({ s }: { s: Submission }) {
  const qc = useQueryClient();
  const [score, setScore] = useState(s.score?.toString() ?? '');
  const grade = useMutation({
    mutationFn: () => gradeSubmission(s.id, Number(score)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['submissions'] }),
  });
  const valid = score !== '' && Number(score) >= 0 && Number(score) <= 10;

  return (
    <tr>
      <td>{s.student}</td>
      <td>{s.assignment}</td>
      <td>{s.submittedAt}</td>
      <td>
        <input
          className="score"
          type="number"
          min={0}
          max={10}
          step={0.25}
          value={score}
          onChange={(e) => setScore(e.target.value)}
          aria-label={`Điểm của ${s.student}`}
        />
      </td>
      <td>
        <button className="btn" disabled={!valid || grade.isPending} onClick={() => grade.mutate()}>
          {grade.isPending ? 'Đang lưu…' : 'Lưu điểm'}
        </button>
        {grade.isError && <span className="state-error"> Lưu lỗi, thử lại</span>}
      </td>
    </tr>
  );
}

export default function Grading() {
  const { data, isLoading, isError } = useQuery({ queryKey: ['submissions'], queryFn: getSubmissions });

  return (
    <>
      <h1>Chấm bài</h1>
      {isLoading && <Loading />}
      {isError && <ErrorBox />}
      {data && data.length === 0 && <Empty text="Chưa có bài nộp nào cần chấm." />}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Sinh viên</th><th>Bài tập</th><th>Nộp lúc</th><th>Điểm (0-10)</th><th></th></tr>
            </thead>
            <tbody>{data.map((s) => <Row key={s.id} s={s} />)}</tbody>
          </table>
        </div>
      )}
    </>
  );
}
