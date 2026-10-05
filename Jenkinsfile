pipeline {
  agent any
  triggers { pollSCM('H/2 * * * *') }
  options {
    timestamps()
    disableConcurrentBuilds()
    skipDefaultCheckout(true)
    timeout(time: 20, unit: 'MINUTES')
    buildDiscarder(logRotator(numToKeepStr: '10'))
  }
  stages {
    stage('Checkout') {
      steps {
        deleteDir()
        checkout scm
        script {
          env.RELEASE = sh(script: 'git rev-parse --short=12 HEAD', returnStdout: true).trim() + '-' + env.BUILD_NUMBER
          env.IMAGE = 'lab-payments:' + env.RELEASE
          env.TEST_IMAGE = 'lab-payments-tests:' + env.RELEASE
          env.TEST_CONTAINER = 'lab-tests-' + env.RELEASE
        }
      }
    }
    stage('Build') {
      steps { sh 'docker build --target build -t "$TEST_IMAGE" .' }
    }
    stage('Unit tests') {
      steps {
        script {
          try {
            def result = sh(returnStatus: true, script: '''
              docker run --name "$TEST_CONTAINER" "$TEST_IMAGE" \
                dotnet test DevOpsLab.slnx -c Release --no-build \
                --logger "trx;LogFileName=tests.trx" --results-directory /results
            ''')
            sh 'mkdir -p TestResults && docker cp "$TEST_CONTAINER:/results/." TestResults/'
            if (result != 0) { error('Unit tests failed; deployment stopped.') }
          } finally {
            sh 'docker rm -f "$TEST_CONTAINER" >/dev/null 2>&1 || true'
          }
        }
      }
    }
    stage('Runtime image') {
      steps { sh 'docker build --target runtime -t "$IMAGE" .' }
    }
    stage('Load image into practice cluster') {
      steps { sh 'kind load docker-image "$IMAGE" --name devops-lab' }
    }
    stage('Database initialization') {
      steps {
        withCredentials([file(credentialsId: 'lab-kubeconfig', variable: 'KUBECONFIG')]) {
          sh '''
            set -eu
            test "$(kubectl config current-context)" = kind-devops-lab
            kubectl -n devops-lab apply -f k8s/postgres.yaml
            kubectl -n devops-lab rollout status statefulset/postgres --timeout=180s
            sed -e "s|__IMAGE__|$IMAGE|g" -e "s|__RELEASE__|$RELEASE|g" \
              k8s/init-db.yaml > release-schema.yaml
            kubectl -n devops-lab apply -f release-schema.yaml
            kubectl -n devops-lab wait --for=condition=complete "job/schema-$RELEASE" --timeout=120s
          '''
        }
      }
    }
    stage('Deploy') {
      steps {
        withCredentials([file(credentialsId: 'lab-kubeconfig', variable: 'KUBECONFIG')]) {
          sh '''
            set -eu
            sed -e "s|__IMAGE__|$IMAGE|g" -e "s|__RELEASE__|$RELEASE|g" \
              k8s/api.yaml > release-api.yaml
            kubectl -n devops-lab apply -f release-api.yaml
            kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
          '''
        }
      }
    }
    stage('Smoke checks') {
      steps {
        withCredentials([file(credentialsId: 'lab-kubeconfig', variable: 'KUBECONFIG')]) {
          sh '''
            set -eu
            NODE_IP=$(kubectl get node devops-lab-control-plane -o jsonpath='{.status.addresses[?(@.type=="InternalIP")].address}')
            curl --fail --silent --show-error \
              --connect-timeout 5 --max-time 10 \
              --retry 5 --retry-delay 3 --retry-all-errors \
              "http://$NODE_IP:30080/health/ready"

            curl --fail --silent --show-error \
              --connect-timeout 5 --max-time 10 \
              --retry 5 --retry-delay 3 --retry-all-errors \
              "http://$NODE_IP:30080/version"
            docker run --rm -i --network kind python:3.12-alpine \
              python - "http://$NODE_IP:30080" < scripts/smoke.py
            kubectl -n devops-lab get deployment,pods,services
          '''
        }
      }
    }
  }
  post {
    always {
      archiveArtifacts artifacts: 'TestResults/**/*.trx,release-*.yaml', allowEmptyArchive: true
    }
  }
}
