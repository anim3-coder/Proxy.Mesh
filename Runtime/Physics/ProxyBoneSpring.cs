using TriInspector;
using UnityEngine;

public class ProxyBoneSpring : MonoBehaviour
{
    [System.Serializable]
    public class ChildData
    {
        public Transform child => body.transform;
        public Rigidbody body;                 // опциональный кинематический Rigidbody
        public float mass => body.mass;

        public float stiffness = 10f;
        public float damping = 2f;
        public float mult = 1f;                // множитель для линейной силы инерции
        public float rotMult = 1f;              // множитель для вращательных сил
        public float maxAngle = 45f;             // максимальный угол отклонения (градусы)
        public float restitution = 1f;           // упругость отскока (0 - неупругий, 1 - упругий)

        // Внутренние переменные состояния
        public Vector3 targetLocalPos;           // равновесная локальная позиция
        public Vector3 targetLocalRot;
        public Vector3 velocity;                  // локальная скорость (касательная)
        public float radius;

        [ShowInInspector]
        public Vector3 linearVelocity => body ? body.linearVelocity : Vector3.zero;
    }

    [SerializeField] private ChildData[] children;

    private Vector3 prevParentPosition;
    private Vector3 parentVelocity;

    private Quaternion prevParentRotation;
    private Vector3 parentAngularVelocityLocal;

    private void OnEnable()
    {
        prevParentPosition = transform.position;
        parentVelocity = Vector3.zero;

        prevParentRotation = transform.rotation;
        parentAngularVelocityLocal = Vector3.zero;

        foreach (var data in children)
        {
            if (data.child != null)
            {
                data.targetLocalPos = data.child.localPosition;
                data.targetLocalRot = data.child.localEulerAngles;
                data.velocity = Vector3.zero;
                data.radius = data.child.localPosition.magnitude;
            }
        }
    }

    private void FixedUpdate()
    {
        // Линейное ускорение родителя в локальных координатах
        Vector3 newParentVelocity = (transform.position - prevParentPosition) / Time.fixedDeltaTime;
        Vector3 worldParentAccel = (newParentVelocity - parentVelocity) / Time.fixedDeltaTime;
        Vector3 localParentAccel = transform.InverseTransformDirection(worldParentAccel);
        prevParentPosition = transform.position;
        parentVelocity = newParentVelocity;

        // Угловая скорость и ускорение родителя в локальных координатах
        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(prevParentRotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        Vector3 worldAngularVelocity = (Mathf.Abs(angle) > Mathf.Epsilon)
            ? axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime)
            : Vector3.zero;

        Vector3 localAngularVelocity = Quaternion.Inverse(transform.rotation) * worldAngularVelocity;
        Vector3 localAngularAccel = (localAngularVelocity - parentAngularVelocityLocal) / Time.fixedDeltaTime;

        prevParentRotation = transform.rotation;
        parentAngularVelocityLocal = localAngularVelocity;

        foreach (var data in children)
        {
            if (data.child == null) continue;

            Transform child = data.child;
            Vector3 targetLocalPos = data.targetLocalPos;
            child.localEulerAngles = data.targetLocalRot;
            Vector3 velocity = data.velocity;
            float mass = data.mass;
            float stiffness = data.stiffness;
            float damping = data.damping;
            float mult = data.mult;
            float rotMult = data.rotMult;
            float maxAngle = data.maxAngle;
            float restitution = data.restitution;

            Vector3 currentLocalPos = child.localPosition;
            float radius = data.radius;
            if (radius < Mathf.Epsilon) continue; // защита от деления на ноль

            Vector3 rDir = currentLocalPos.normalized;

            // Убедимся, что скорость касательная (на всякий случай)
            velocity -= Vector3.Dot(velocity, rDir) * rDir;

            // Вычисляем силы в локальных координатах
            Vector3 springForce = -stiffness * (currentLocalPos - targetLocalPos);
            Vector3 dampingForce = -damping * velocity;
            Vector3 inertialForce = -mass * localParentAccel * mult;

            Vector3 centrifugal = -mass * Vector3.Cross(localAngularVelocity, Vector3.Cross(localAngularVelocity, currentLocalPos));
            Vector3 coriolis = -2f * mass * Vector3.Cross(localAngularVelocity, velocity);
            Vector3 euler = -mass * Vector3.Cross(localAngularAccel, currentLocalPos);
            Vector3 rotationalForces = rotMult * (centrifugal + coriolis + euler);

            Vector3 totalForce = springForce + dampingForce + inertialForce + rotationalForces;

            // Проецируем силу на касательную плоскость (перпендикулярно rDir)
            Vector3 tangentialForce = totalForce - Vector3.Dot(totalForce, rDir) * rDir;

            // Интегрируем скорость (только касательная сила)
            velocity += tangentialForce / mass * Time.fixedDeltaTime;

            // Обновляем позицию (предварительная, до проверки угла)
            Vector3 newLocalPos = currentLocalPos + velocity * Time.fixedDeltaTime;

            // Проверка на превышение максимального угла
            Vector3 targetDir = targetLocalPos.normalized;
            Vector3 currentDir = newLocalPos.normalized;
            float cosAngle = Vector3.Dot(currentDir, targetDir);
            float angleDeg = Mathf.Acos(cosAngle) * Mathf.Rad2Deg;

            if (angleDeg > maxAngle)
            {
                // Ось вращения от targetDir к currentDir
                Vector3 axisRot = Vector3.Cross(targetDir, currentDir).normalized;
                if (axisRot.sqrMagnitude > 0.001f)
                {
                    Vector3 clampedDir = Quaternion.AngleAxis(maxAngle, axisRot) * targetDir;
                    newLocalPos = clampedDir * radius;

                    // Касательное направление, увеличивающее угол (в точке границы)
                    Vector3 tangentDir = Vector3.Cross(axisRot, clampedDir).normalized;

                    // Разлагаем скорость
                    float vTang = Vector3.Dot(velocity, tangentDir);
                    Vector3 vPerp = velocity - vTang * tangentDir;
                    // Применяем коэффициент восстановления
                    vTang = -restitution * vTang;
                    velocity = vPerp + vTang * tangentDir;
                }
                else
                {
                    // Угол близок к 180°, оставляем как есть (не должно случаться при разумных углах)
                    newLocalPos = currentDir * radius;
                }
            }

            // Итоговая локальная позиция (нормализованная до радиуса)
            Vector3 finalLocalPos = newLocalPos.normalized * radius;

            // Применяем позицию с учётом наличия Rigidbody
            if (data.body != null && data.body.isKinematic)
            {
                // Преобразуем локальную позицию в мировую и используем MovePosition
                Vector3 worldPos = transform.TransformPoint(finalLocalPos);
                data.body.MovePosition(worldPos);
            }
            else
            {
                // Прямое присвоение локальной позиции (если Rigidbody нет или не кинематический)
                child.localPosition = finalLocalPos;
            }

            // Сохраняем обновлённую скорость в данных
            data.velocity = velocity;
        }
    }
}