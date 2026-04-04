using UnityEngine;

public class SpringBone : MonoBehaviour
{
    [Header("Основные параметры")]
    public Transform parentTransform; // Первая точка (родитель)
    public Transform childTransform;  // Вторая точка (дочерняя)

    [Header("Параметры пружины")]
    [SerializeField] private float stiffness = 100f;    // Жесткость пружины
    [SerializeField] private float damping = 10f;       // Демпфирование
    [SerializeField] private float mass = 1f;           // Масса

    [Header("Ограничения")]
    [SerializeField] private bool useLengthLimit = true;
    [SerializeField] private float maxLength = 1f;      // Максимальная длина
    [SerializeField] private float minLength = 0.5f;    // Минимальная длина

    [Header("Внешние силы")]
    [SerializeField] private Vector3 gravity = new Vector3(0, -9.81f, 0);
    [SerializeField] private float airResistance = 0.1f; // Сопротивление воздуха

    // Внутренние переменные
    private Vector3 _velocity;
    private Vector3 _previousParentPosition;
    private Quaternion _previousParentRotation;
    private Vector3 _restLocalPosition; // Относительная позиция в покое

    void Start()
    {
        if (parentTransform == null || childTransform == null)
        {
            Debug.LogError("Не заданы Transform точки!");
            enabled = false;
            return;
        }

        // Инициализация
        _velocity = Vector3.zero;
        _previousParentPosition = parentTransform.position;
        _previousParentRotation = parentTransform.rotation;

        // Сохраняем локальную позицию относительно родителя
        _restLocalPosition = parentTransform.InverseTransformPoint(childTransform.position);
    }

    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        // 1. Вычисляем целевую позицию (с учетом вращения родителя)
        Vector3 targetPosition = parentTransform.TransformPoint(_restLocalPosition);

        // 2. Получаем текущую позицию
        Vector3 currentPosition = childTransform.position;

        // 3. Вычисляем внешние силы
        Vector3 externalForces = gravity * mass;

        // 4. Сила пружины (Закон Гука)
        Vector3 springForce = (targetPosition - currentPosition) * stiffness;

        // 5. Сила демпфирования (пропорциональна скорости)
        Vector3 dampingForce = -_velocity * damping;

        // 6. Сопротивление воздуха
        Vector3 airDrag = -_velocity * airResistance;

        // 7. Суммируем все силы
        Vector3 totalForce = springForce + dampingForce + externalForces + airDrag;

        // 8. Интегрирование (ускорение -> скорость -> позиция)
        Vector3 acceleration = totalForce / mass;
        _velocity += acceleration * deltaTime;
        Vector3 newPosition = currentPosition + _velocity * deltaTime;

        // 9. Применяем ограничения по длине
        if (useLengthLimit)
        {
            newPosition = ApplyLengthConstraints(newPosition, targetPosition);
        }

        // 10. Обновляем позицию
        childTransform.position = newPosition;

        // 11. Обновляем вращение (опционально)
        UpdateRotation();

        // 12. Сохраняем состояние для следующего кадра
        _previousParentPosition = parentTransform.position;
        _previousParentRotation = parentTransform.rotation;
    }

    private Vector3 ApplyLengthConstraints(Vector3 newPosition, Vector3 targetPosition)
    {
        Vector3 direction = newPosition - targetPosition;
        float distance = direction.magnitude;

        if (distance > maxLength)
        {
            newPosition = targetPosition + direction.normalized * maxLength;
            // Корректируем скорость (упругое столкновение)
            float velocityAlongDirection = Vector3.Dot(_velocity, direction.normalized);
            if (velocityAlongDirection > 0)
            {
                _velocity -= direction.normalized * velocityAlongDirection * 0.5f;
            }
        }
        else if (distance < minLength && distance > 0.001f)
        {
            newPosition = targetPosition + direction.normalized * minLength;
            // Корректируем скорость
            float velocityAlongDirection = Vector3.Dot(_velocity, direction.normalized);
            if (velocityAlongDirection < 0)
            {
                _velocity -= direction.normalized * velocityAlongDirection * 0.5f;
            }
        }

        return newPosition;
    }

    private void UpdateRotation()
    {
        // Плавное следование вращению родителя
        Quaternion targetRotation = parentTransform.rotation;
        Quaternion currentRotation = childTransform.rotation;

        // Интерполяция с учетом жесткости
        float rotationSpeed = stiffness * 0.01f;
        childTransform.rotation = Quaternion.Slerp(
            currentRotation,
            targetRotation,
            rotationSpeed * Time.fixedDeltaTime
        );
    }

    // Метод для ручной настройки параметров
    public void SetParameters(float newStiffness, float newDamping, float newMass)
    {
        stiffness = Mathf.Max(0, newStiffness);
        damping = Mathf.Max(0, newDamping);
        mass = Mathf.Max(0.001f, newMass);
    }

    // Сброс состояния пружины
    public void ResetSpring()
    {
        _velocity = Vector3.zero;
        childTransform.position = parentTransform.TransformPoint(_restLocalPosition);
        childTransform.rotation = parentTransform.rotation;
    }

    // Визуализация в редакторе
    void OnDrawGizmosSelected()
    {
        if (parentTransform != null && childTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(parentTransform.position, childTransform.position);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(parentTransform.position, 0.05f);

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(childTransform.position, 0.05f);
        }
    }
}